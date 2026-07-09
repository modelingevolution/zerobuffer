#include <gtest/gtest.h>

#include "zerobuffer/latest_frame.h"
#include "zerobuffer/platform.h"

#include <atomic>
#include <chrono>
#include <cstring>
#include <set>
#include <string>
#include <thread>
#include <vector>

using namespace zerobuffer;
using namespace std::chrono_literals;

namespace {

// Unique buffer name per test so parallel/repeated runs never collide, and a
// pre-clean in case a previous crashed run left a segment behind.
std::string make_name(const char* suffix) {
    std::string name = "/lf_test_" + std::to_string(platform::get_current_pid()) + "_" + suffix;
    SharedMemory::remove(name);
    return name;
}

// A frame whose every byte equals (sequence & 0xFF). A tear-free read must find
// all bytes identical; a torn read (mixed laps) would show two values.
void fill_pattern(uint8_t* p, size_t size, uint64_t seq) {
    std::memset(p, static_cast<int>(seq & 0xFF), size);
}

bool all_bytes_equal(const uint8_t* p, size_t size, uint8_t value) {
    for (size_t i = 0; i < size; ++i) {
        if (p[i] != value) return false;
    }
    return true;
}

// Test-side capture of one zero-copy read: records the slot pointer, size and
// sequence, and (optionally) copies the bytes out FOR ASSERTIONS ONLY — the
// primitive itself never copies.
struct Captured {
    bool valid = false;
    const void* ptr = nullptr;
    size_t size = 0;
    uint64_t sequence = 0;
    std::vector<uint8_t> bytes;
};

Captured read_capture(LatestFrameReader& r, std::chrono::milliseconds t, bool copy_bytes = true) {
    Captured c;
    c.valid = r.read_latest_into(
        [&](const uint8_t* src, size_t size, uint64_t seq) {
            c.ptr = src;
            c.size = size;
            c.sequence = seq;
            if (copy_bytes) c.bytes.assign(src, src + size);
        },
        t);
    return c;
}

void publish_pattern(LatestFrameWriter& w, uint64_t seq, size_t size) {
    uint8_t* slot = w.get_slot();
    fill_pattern(slot, size, seq);
    w.publish(seq, size);
}

} // namespace

// FR-7: the writer creates and publishes with NO reader present (the ADR-1
// preroll bug must be impossible for this primitive).
TEST(LatestFrameTest, WriterCreatesAndPublishesWithNoReader) {
    std::string name = make_name("no_reader");
    LatestFrameWriter writer(name, 4096, 3);
    for (uint64_t seq = 0; seq < 10; ++seq) {
        uint8_t* slot = writer.get_slot();
        ASSERT_NE(slot, nullptr);
        fill_pattern(slot, 4096, seq);
        writer.publish(seq, 4096);  // never blocks, no reader exists
    }
    SUCCEED();
}

TEST(LatestFrameTest, SlotCountRaisedToMinimum) {
    std::string name = make_name("min_slots");
    LatestFrameWriter writer(name, 1024, 1);
    EXPECT_EQ(writer.slot_count(), LATEST_FRAME_MIN_SLOTS);
}

TEST(LatestFrameTest, ZeroSlotSizeRejected) {
    std::string name = make_name("zero_size");
    EXPECT_THROW(LatestFrameWriter(name, 0, 3), ZeroBufferException);
}

// Reader attaching AFTER the writer gets the latest published frame (zero-copy).
TEST(LatestFrameTest, ReaderAttachesAfterWriterGetsLatest) {
    std::string name = make_name("attach_after");
    LatestFrameWriter writer(name, 4096, 3);
    for (uint64_t seq = 0; seq <= 5; ++seq) publish_pattern(writer, seq, 4096);

    LatestFrameReader reader(name);
    Captured f = read_capture(reader, 1000ms);
    ASSERT_TRUE(f.valid);
    EXPECT_EQ(f.sequence, 5u);
    EXPECT_EQ(f.size, 4096u);
    EXPECT_TRUE(all_bytes_equal(f.bytes.data(), f.bytes.size(), 5 & 0xFF));
}

// Newest-frame-wins: under a burst, a late reader skips intermediates.
TEST(LatestFrameTest, NewestWinsSkipsIntermediates) {
    std::string name = make_name("newest_wins");
    LatestFrameWriter writer(name, 1024, 3);
    LatestFrameReader reader(name);
    for (uint64_t seq = 0; seq < 100; ++seq) publish_pattern(writer, seq, 1024);

    Captured f = read_capture(reader, 1000ms);
    ASSERT_TRUE(f.valid);
    EXPECT_EQ(f.sequence, 99u);  // landed on the newest, not seq 0
}

// Zero-copy: the pointer handed to consume moves through the slot ring (proving
// no per-frame copy into a single reused buffer) and is bounded by slot_count
// (proving no per-frame allocation).
TEST(LatestFrameTest, ZeroCopyPointerRotatesThroughSlots) {
    std::string name = make_name("zerocopy");
    LatestFrameWriter writer(name, 4096, 3);
    LatestFrameReader reader(name);

    std::set<const void*> ptrs;
    for (uint64_t seq = 0; seq < 20; ++seq) {
        publish_pattern(writer, seq, 4096);
        Captured f = read_capture(reader, 1000ms, /*copy_bytes=*/false);
        ASSERT_TRUE(f.valid);
        ASSERT_NE(f.ptr, nullptr);
        EXPECT_EQ(f.sequence, seq);
        ptrs.insert(f.ptr);
    }
    // >1 distinct pointer: not a single reused scratch buffer (that would be 1).
    EXPECT_GT(ptrs.size(), 1u);
    // <= slot_count: a fixed ring, no per-frame allocation.
    EXPECT_LE(ptrs.size(), writer.slot_count());
}

// Segment absent for the reader -> read returns false (poll/retry), consume is
// never invoked, never crashes or blocks past the timeout.
TEST(LatestFrameTest, ReaderToleratesAbsentSegment) {
    std::string name = make_name("absent");
    LatestFrameReader reader(name);  // nothing created it
    EXPECT_FALSE(reader.is_attached());

    bool consumed = false;
    auto start = std::chrono::steady_clock::now();
    bool ok = reader.read_latest_into([&](const uint8_t*, size_t, uint64_t) { consumed = true; },
                                      100ms);
    auto elapsed = std::chrono::steady_clock::now() - start;
    EXPECT_FALSE(ok);
    EXPECT_FALSE(consumed);
    EXPECT_GE(elapsed, 90ms);
    EXPECT_LT(elapsed, 2000ms);
}

// Reader detach + re-attach mid-stream; writer is unaffected and keeps cadence.
TEST(LatestFrameTest, ReaderDetachReattachWriterUnaffected) {
    std::string name = make_name("reattach");
    LatestFrameWriter writer(name, 1024, 3);

    {
        LatestFrameReader reader(name);  // attach before any frame, then detach
        EXPECT_FALSE(read_capture(reader, 50ms).valid);
    }
    uint64_t seq = 0;
    publish_pattern(writer, seq++, 1024);

    {
        LatestFrameReader reader(name);
        EXPECT_TRUE(read_capture(reader, 1000ms).valid);
    }
    for (int i = 0; i < 20; ++i) publish_pattern(writer, seq++, 1024);

    LatestFrameReader reader2(name);
    Captured f = read_capture(reader2, 1000ms);
    ASSERT_TRUE(f.valid);
    EXPECT_EQ(f.sequence, seq - 1);
}

// Writer restart (segment recreated, sequence reset) while a reader polls ->
// reader recovers on the next publish.
TEST(LatestFrameTest, WriterRestartReaderRecovers) {
    std::string name = make_name("restart");
    auto writer = std::make_unique<LatestFrameWriter>(name, 1024, 3);
    for (uint64_t seq = 0; seq < 50; ++seq) publish_pattern(*writer, seq, 1024);

    LatestFrameReader reader(name);
    Captured f1 = read_capture(reader, 1000ms);
    ASSERT_TRUE(f1.valid);
    EXPECT_EQ(f1.sequence, 49u);

    writer.reset();  // unlink (invalidates magic)
    writer = std::make_unique<LatestFrameWriter>(name, 1024, 3);  // seq resets from 0

    bool recovered = false;
    for (int i = 0; i < 100 && !recovered; ++i) {
        publish_pattern(*writer, static_cast<uint64_t>(i), 1024);
        Captured f = read_capture(reader, 200ms);
        if (f.valid) {
            EXPECT_TRUE(all_bytes_equal(f.bytes.data(), f.bytes.size(), f.sequence & 0xFF));
            recovered = true;
        }
    }
    EXPECT_TRUE(recovered);
}

// Writer gone -> reader reports producer-gone, no crash/block.
TEST(LatestFrameTest, WriterGoneReportedNoCrash) {
    std::string name = make_name("gone");
    {
        LatestFrameWriter writer(name, 1024, 3);
        publish_pattern(writer, 1, 1024);

        LatestFrameReader reader(name);
        ASSERT_TRUE(read_capture(reader, 1000ms).valid);
        EXPECT_TRUE(reader.is_writer_alive());
    }
    // Segment is now unlinked; a fresh reader cannot attach and reports gone.
    LatestFrameReader reader(name);
    EXPECT_FALSE(reader.is_writer_alive());
    EXPECT_FALSE(read_capture(reader, 100ms).valid);
}

// Oversized frame vs slot_size -> handled without out-of-bounds; the consume
// never sees more than slot_size bytes.
TEST(LatestFrameTest, OversizedFrameClampedNoOverflow) {
    std::string name = make_name("oversize");
    LatestFrameWriter writer(name, 1024, 3);
    uint8_t* slot = writer.get_slot();
    fill_pattern(slot, 1024, 7);
    writer.publish(7, 100000);  // claim way more than slot_size

    LatestFrameReader reader(name);
    Captured f = read_capture(reader, 1000ms);
    ASSERT_TRUE(f.valid);
    EXPECT_LE(f.size, 1024u);  // clamped to slot_size, no OOB
}

// Metadata/caps set + rewritten on change -> reader re-reads (FR-3). Verifies
// the exact ShmCaps wire shape: 4-byte LE length prefix + JSON.
TEST(LatestFrameTest, MetadataSetAndRewrite) {
    std::string name = make_name("metadata");
    LatestFrameWriter writer(name, 1024, 3);

    std::string caps1 = R"({"caps":"video/x-raw,format=GRAY8,width=640,height=480"})";
    writer.set_metadata(caps1.data(), caps1.size());
    publish_pattern(writer, 1, 1024);

    LatestFrameReader reader(name);
    ASSERT_TRUE(read_capture(reader, 1000ms).valid);

    const uint8_t* md = static_cast<const uint8_t*>(reader.get_metadata_raw());
    size_t md_size = reader.get_metadata_size();
    ASSERT_NE(md, nullptr);
    ASSERT_EQ(md_size, 4 + caps1.size());
    uint32_t prefix = static_cast<uint32_t>(md[0]) | (static_cast<uint32_t>(md[1]) << 8) |
                      (static_cast<uint32_t>(md[2]) << 16) | (static_cast<uint32_t>(md[3]) << 24);
    EXPECT_EQ(prefix, caps1.size());
    EXPECT_EQ(0, std::memcmp(md + 4, caps1.data(), caps1.size()));

    std::string caps2 = R"({"caps":"video/x-raw,format=I420,width=1920,height=1080"})";
    writer.set_metadata(caps2.data(), caps2.size());
    publish_pattern(writer, 2, 1024);

    ASSERT_TRUE(read_capture(reader, 1000ms).valid);
    md = static_cast<const uint8_t*>(reader.get_metadata_raw());
    md_size = reader.get_metadata_size();
    ASSERT_EQ(md_size, 4 + caps2.size());
    EXPECT_EQ(0, std::memcmp(md + 4, caps2.data(), caps2.size()));
}

// A frame published BEFORE the first set_metadata -> reader reports caps
// not-ready (null / size 0), never a valid-but-empty caps block (MINOR-2).
TEST(LatestFrameTest, MetadataNotReadyBeforeFirstSet) {
    std::string name = make_name("meta_notready");
    LatestFrameWriter writer(name, 1024, 3);
    publish_pattern(writer, 1, 1024);  // published, but no set_metadata yet

    LatestFrameReader reader(name);
    ASSERT_TRUE(read_capture(reader, 1000ms).valid);
    EXPECT_EQ(reader.get_metadata_raw(), nullptr);
    EXPECT_EQ(reader.get_metadata_size(), 0u);
}

// Tear-free under concurrent write: a reader in a tight read_latest_into loop vs
// a fast writer over many iterations. The COMMITTED frame (read returns true) is
// never torn; the seqlock retry path is exercised (the reader observes and
// rejects mid-lap slots). Zero-copy: consume validates the slot in place.
TEST(LatestFrameTest, TearFreeUnderConcurrentWrite) {
    std::string name = make_name("tearfree");
    constexpr size_t FRAME = 256 * 1024;  // large payload widens the tear window
    constexpr uint64_t ITERATIONS = 20000;

    LatestFrameWriter writer(name, FRAME, 3);  // min slots -> tightest safe window
    std::atomic<bool> stop{false};
    std::atomic<uint64_t> committed_torn{0};
    std::atomic<uint64_t> observed_torn{0};  // mid-lap reads caught by the seqlock
    std::atomic<uint64_t> reads{0};
    std::atomic<bool> reader_ready{false};

    std::thread reader_thread([&]() {
        LatestFrameReader reader(name);
        reader_ready.store(true, std::memory_order_release);
        while (!stop.load(std::memory_order_relaxed)) {
            bool consistent = true;
            bool ok = reader.read_latest_into(
                [&](const uint8_t* src, size_t size, uint64_t seq) {
                    uint8_t expected = static_cast<uint8_t>(seq & 0xFF);
                    bool c = all_bytes_equal(src, size, expected);
                    // Widen the read window so a lapping writer can overwrite this
                    // slot mid-read, then re-scan: this deterministically exercises
                    // the seqlock retry path over many iterations.
                    volatile int sink = 0;
                    for (int spin = 0; spin < 4000; ++spin) {
                        sink = sink + 1;
                    }
                    (void)sink;
                    if (c && !all_bytes_equal(src, size, expected)) c = false;
                    if (!c) observed_torn.fetch_add(1, std::memory_order_relaxed);
                    consistent = c;
                },
                50ms);
            if (!ok) continue;
            if (!consistent) committed_torn.fetch_add(1, std::memory_order_relaxed);
            reads.fetch_add(1, std::memory_order_relaxed);
        }
    });

    while (!reader_ready.load(std::memory_order_acquire)) {
        std::this_thread::yield();
    }
    for (uint64_t seq = 0; seq < ITERATIONS; ++seq) publish_pattern(writer, seq, FRAME);
    std::this_thread::sleep_for(50ms);
    stop.store(true, std::memory_order_relaxed);
    reader_thread.join();

    EXPECT_EQ(committed_torn.load(), 0u) << "a torn frame was committed";
    EXPECT_GT(reads.load(), 0u) << "reader never observed a frame";
    EXPECT_GT(observed_torn.load(), 0u) << "seqlock retry path was never exercised";
}

// Two readers on one segment both read the newest tear-free (1..N readers).
TEST(LatestFrameTest, MultipleReaders) {
    std::string name = make_name("multi_reader");
    LatestFrameWriter writer(name, 4096, 4);
    for (uint64_t seq = 0; seq <= 3; ++seq) publish_pattern(writer, seq, 4096);

    LatestFrameReader r1(name);
    LatestFrameReader r2(name);
    Captured f1 = read_capture(r1, 1000ms);
    Captured f2 = read_capture(r2, 1000ms);
    ASSERT_TRUE(f1.valid);
    ASSERT_TRUE(f2.valid);
    EXPECT_EQ(f1.sequence, 3u);
    EXPECT_EQ(f2.sequence, 3u);
    EXPECT_TRUE(all_bytes_equal(f1.bytes.data(), f1.bytes.size(), 3));
    EXPECT_TRUE(all_bytes_equal(f2.bytes.data(), f2.bytes.size(), 3));
}

// A live writer owning the segment cannot be displaced by a second writer.
TEST(LatestFrameTest, SecondWriterOnLiveSegmentRejected) {
    std::string name = make_name("double_writer");
    LatestFrameWriter writer(name, 1024, 3);
    EXPECT_THROW(LatestFrameWriter(name, 1024, 3), ZeroBufferException);
}

// A stale segment left by a dead writer is reclaimed on create. Build a genuine
// stale segment through the platform layer (magic + dead pid, never unlinked),
// so LatestFrameWriter::create() hits EEXIST and must reclaim it.
TEST(LatestFrameTest, StaleSegmentReclaimedOnCreate) {
    std::string name = make_name("stale");
    {
        auto shm = SharedMemory::create(name, 65536);
        auto* h = static_cast<LatestFrameSharedHeader*>(shm->data());
        std::memset(h, 0, sizeof(*h));
        h->version = LATEST_FRAME_VERSION;
        h->slot_count = 3;
        h->slot_size = 1024;
        h->slot_stride = 1088;
        h->slots_offset = 8320;
        h->metadata_offset = sizeof(LatestFrameSharedHeader);
        h->metadata_capacity = LATEST_FRAME_METADATA_CAPACITY;
        h->publish_index = -1;
        h->writer_pid = 0;  // dead / no owner -> stale
        std::atomic_ref<uint32_t>(h->magic).store(LATEST_FRAME_MAGIC, std::memory_order_release);
        // shm mapping dropped here WITHOUT shm_unlink -> the named segment persists.
    }

    LatestFrameWriter fresh(name, 1024, 3);
    publish_pattern(fresh, 9, 1024);

    LatestFrameReader reader(name);
    Captured f = read_capture(reader, 1000ms);
    ASSERT_TRUE(f.valid);
    EXPECT_EQ(f.sequence, 9u);
}
