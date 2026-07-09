#include <gtest/gtest.h>

#include "zerobuffer/latest_frame.h"
#include "zerobuffer/platform.h"

#include <atomic>
#include <chrono>
#include <cstring>
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

// Slot count below the triple-slot minimum is raised, not accepted verbatim.
TEST(LatestFrameTest, SlotCountRaisedToMinimum) {
    std::string name = make_name("min_slots");
    LatestFrameWriter writer(name, 1024, 1);
    EXPECT_EQ(writer.slot_count(), LATEST_FRAME_MIN_SLOTS);
}

TEST(LatestFrameTest, ZeroSlotSizeRejected) {
    std::string name = make_name("zero_size");
    EXPECT_THROW(LatestFrameWriter(name, 0, 3), ZeroBufferException);
}

// Reader attaching AFTER the writer gets the latest published frame.
TEST(LatestFrameTest, ReaderAttachesAfterWriterGetsLatest) {
    std::string name = make_name("attach_after");
    LatestFrameWriter writer(name, 4096, 3);
    for (uint64_t seq = 0; seq <= 5; ++seq) {
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, 4096, seq);
        writer.publish(seq, 4096);
    }

    LatestFrameReader reader(name);
    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_EQ(f.sequence(), 5u);
    EXPECT_EQ(f.size(), 4096u);
    EXPECT_TRUE(all_bytes_equal(static_cast<const uint8_t*>(f.data()), f.size(), 5 & 0xFF));
}

// Newest-frame-wins: under a burst, a late reader skips intermediates.
TEST(LatestFrameTest, NewestWinsSkipsIntermediates) {
    std::string name = make_name("newest_wins");
    LatestFrameWriter writer(name, 1024, 3);
    LatestFrameReader reader(name);

    for (uint64_t seq = 0; seq < 100; ++seq) {
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, 1024, seq);
        writer.publish(seq, 1024);
    }

    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_EQ(f.sequence(), 99u);  // landed on the newest, not seq 0
}

// Segment absent for the reader -> read_latest returns invalid (poll/retry),
// never crashes or blocks past the timeout.
TEST(LatestFrameTest, ReaderToleratesAbsentSegment) {
    std::string name = make_name("absent");
    LatestFrameReader reader(name);  // nothing created it
    EXPECT_FALSE(reader.is_attached());

    auto start = std::chrono::steady_clock::now();
    LatestFrame f = reader.read_latest(100ms);
    auto elapsed = std::chrono::steady_clock::now() - start;
    EXPECT_FALSE(f.valid());
    EXPECT_GE(elapsed, 90ms);
    EXPECT_LT(elapsed, 2000ms);
}

// Reader detach + re-attach mid-stream; writer is unaffected and keeps cadence.
TEST(LatestFrameTest, ReaderDetachReattachWriterUnaffected) {
    std::string name = make_name("reattach");
    LatestFrameWriter writer(name, 1024, 3);

    {
        LatestFrameReader reader(name);  // attach before any frame, then detach
        EXPECT_FALSE(reader.read_latest(50ms).valid());
    }
    uint64_t seq = 0;
    auto publish_one = [&](uint64_t s) {
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, 1024, s);
        writer.publish(s, 1024);
    };
    publish_one(seq++);

    {
        LatestFrameReader reader(name);
        LatestFrame f = reader.read_latest(1000ms);
        ASSERT_TRUE(f.valid());
    }
    // Writer keeps publishing after the reader detached.
    for (int i = 0; i < 20; ++i) publish_one(seq++);

    LatestFrameReader reader2(name);
    LatestFrame f = reader2.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_EQ(f.sequence(), seq - 1);
}

// Writer restart (segment recreated, sequence reset) while a reader polls ->
// reader recovers on the next publish.
TEST(LatestFrameTest, WriterRestartReaderRecovers) {
    std::string name = make_name("restart");
    auto writer = std::make_unique<LatestFrameWriter>(name, 1024, 3);
    for (uint64_t seq = 0; seq < 50; ++seq) {
        uint8_t* slot = writer->get_slot();
        fill_pattern(slot, 1024, seq);
        writer->publish(seq, 1024);
    }

    LatestFrameReader reader(name);
    LatestFrame f1 = reader.read_latest(1000ms);
    ASSERT_TRUE(f1.valid());
    EXPECT_EQ(f1.sequence(), 49u);

    // Restart: destroy the writer (unlinks) and create a fresh one (seq from 0).
    writer.reset();
    writer = std::make_unique<LatestFrameWriter>(name, 1024, 3);

    // Reader recovers and reads the new segment's frames.
    bool recovered = false;
    for (int i = 0; i < 100 && !recovered; ++i) {
        uint8_t* slot = writer->get_slot();
        fill_pattern(slot, 1024, static_cast<uint64_t>(i));
        writer->publish(static_cast<uint64_t>(i), 1024);
        LatestFrame f = reader.read_latest(200ms);
        if (f.valid()) {
            EXPECT_TRUE(all_bytes_equal(static_cast<const uint8_t*>(f.data()), f.size(),
                                        f.sequence() & 0xFF));
            recovered = true;
        }
    }
    EXPECT_TRUE(recovered);
}

// Writer gone / stale heartbeat -> reader reports producer-gone, no crash/block.
TEST(LatestFrameTest, WriterGoneReportedNoCrash) {
    std::string name = make_name("gone");
    {
        LatestFrameWriter writer(name, 1024, 3);
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, 1024, 1);
        writer.publish(1, 1024);

        LatestFrameReader reader(name);
        LatestFrame f = reader.read_latest(1000ms);
        ASSERT_TRUE(f.valid());
        EXPECT_TRUE(reader.is_writer_alive());
        // Writer destroyed at end of scope, before the reader below.
        LatestFrameReader probe(name);
        EXPECT_TRUE(probe.is_writer_alive());
    }
    // Segment is now unlinked; a fresh reader cannot attach and reports gone.
    LatestFrameReader reader(name);
    EXPECT_FALSE(reader.is_writer_alive());
    LatestFrame f = reader.read_latest(100ms);
    EXPECT_FALSE(f.valid());
}

// Oversized frame vs slot_size -> handled without out-of-bounds; the reader
// never sees more than slot_size bytes.
TEST(LatestFrameTest, OversizedFrameClampedNoOverflow) {
    std::string name = make_name("oversize");
    LatestFrameWriter writer(name, 1024, 3);
    uint8_t* slot = writer.get_slot();
    fill_pattern(slot, 1024, 7);
    writer.publish(7, 100000);  // claim way more than slot_size

    LatestFrameReader reader(name);
    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_LE(f.size(), 1024u);  // clamped to slot_size, no OOB
}

// Metadata/caps set + rewritten on change -> reader re-reads (FR-3). Verifies
// the exact ShmCaps wire shape: 4-byte LE length prefix + JSON.
TEST(LatestFrameTest, MetadataSetAndRewrite) {
    std::string name = make_name("metadata");
    LatestFrameWriter writer(name, 1024, 3);

    std::string caps1 = R"({"caps":"video/x-raw,format=GRAY8,width=640,height=480"})";
    writer.set_metadata(caps1.data(), caps1.size());
    uint8_t* slot = writer.get_slot();
    fill_pattern(slot, 1024, 1);
    writer.publish(1, 1024);

    LatestFrameReader reader(name);
    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());

    const uint8_t* md = static_cast<const uint8_t*>(reader.get_metadata_raw());
    size_t md_size = reader.get_metadata_size();
    ASSERT_NE(md, nullptr);
    ASSERT_EQ(md_size, 4 + caps1.size());
    uint32_t prefix = static_cast<uint32_t>(md[0]) | (static_cast<uint32_t>(md[1]) << 8) |
                      (static_cast<uint32_t>(md[2]) << 16) | (static_cast<uint32_t>(md[3]) << 24);
    EXPECT_EQ(prefix, caps1.size());
    EXPECT_EQ(0, std::memcmp(md + 4, caps1.data(), caps1.size()));

    // Rewrite caps on a change; the reader re-reads on the next poll.
    std::string caps2 = R"({"caps":"video/x-raw,format=I420,width=1920,height=1080"})";
    writer.set_metadata(caps2.data(), caps2.size());
    slot = writer.get_slot();
    fill_pattern(slot, 1024, 2);
    writer.publish(2, 1024);

    f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
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
    uint8_t* slot = writer.get_slot();
    fill_pattern(slot, 1024, 1);
    writer.publish(1, 1024);  // published, but no set_metadata yet

    LatestFrameReader reader(name);
    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_EQ(reader.get_metadata_raw(), nullptr);
    EXPECT_EQ(reader.get_metadata_size(), 0u);
}

// Tear-free read under concurrent write: a reader loop vs a fast writer over
// many iterations must never return a torn (mixed-lap) frame.
TEST(LatestFrameTest, TearFreeUnderConcurrentWrite) {
    std::string name = make_name("tearfree");
    constexpr size_t FRAME = 256 * 1024;  // large payload widens the tear window
    constexpr uint64_t ITERATIONS = 20000;

    LatestFrameWriter writer(name, FRAME, 3);
    std::atomic<bool> stop{false};
    std::atomic<uint64_t> torn{0};
    std::atomic<uint64_t> reads{0};

    std::thread reader_thread([&]() {
        LatestFrameReader reader(name);
        while (!stop.load(std::memory_order_relaxed)) {
            LatestFrame f = reader.read_latest(50ms);
            if (!f.valid()) continue;
            const uint8_t* p = static_cast<const uint8_t*>(f.data());
            uint8_t expected = static_cast<uint8_t>(f.sequence() & 0xFF);
            if (!all_bytes_equal(p, f.size(), expected)) {
                torn.fetch_add(1, std::memory_order_relaxed);
            }
            reads.fetch_add(1, std::memory_order_relaxed);
        }
    });

    for (uint64_t seq = 0; seq < ITERATIONS; ++seq) {
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, FRAME, seq);
        writer.publish(seq, FRAME);
    }
    // Let the reader drain a little, then stop.
    std::this_thread::sleep_for(50ms);
    stop.store(true, std::memory_order_relaxed);
    reader_thread.join();

    EXPECT_EQ(torn.load(), 0u) << "torn frames observed";
    EXPECT_GT(reads.load(), 0u) << "reader never observed a frame";
}

// Two readers on one segment both read tear-free (1..N readers, FR).
TEST(LatestFrameTest, MultipleReaders) {
    std::string name = make_name("multi_reader");
    LatestFrameWriter writer(name, 4096, 4);
    for (uint64_t seq = 0; seq <= 3; ++seq) {
        uint8_t* slot = writer.get_slot();
        fill_pattern(slot, 4096, seq);
        writer.publish(seq, 4096);
    }

    LatestFrameReader r1(name);
    LatestFrameReader r2(name);
    LatestFrame f1 = r1.read_latest(1000ms);
    LatestFrame f2 = r2.read_latest(1000ms);
    ASSERT_TRUE(f1.valid());
    ASSERT_TRUE(f2.valid());
    EXPECT_EQ(f1.sequence(), 3u);
    EXPECT_EQ(f2.sequence(), 3u);
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

    // Hand-craft a leftover segment: valid magic, but writer_pid = 0 (which the
    // reclaim path treats as no live owner). It is NOT unlinked, mimicking a
    // writer that crashed without cleanup.
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

    // A new writer must reclaim the stale name and create cleanly.
    LatestFrameWriter fresh(name, 1024, 3);
    uint8_t* slot = fresh.get_slot();
    fill_pattern(slot, 1024, 9);
    fresh.publish(9, 1024);

    LatestFrameReader reader(name);
    LatestFrame f = reader.read_latest(1000ms);
    ASSERT_TRUE(f.valid());
    EXPECT_EQ(f.sequence(), 9u);
}
