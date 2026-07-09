#ifndef ZEROBUFFER_LATEST_FRAME_H
#define ZEROBUFFER_LATEST_FRAME_H

// Producer-owned, consumer-optional, newest-frame-wins display buffer
// (Epic 020 / feature-002, ADR-1 + ADR-11). Purely additive: built on
// zerobuffer's platform layer (SharedMemory / process_exists) and does NOT
// touch the SPSC Reader/Writer, so the AI path's SPSC contract is unchanged.
//
// The writer creates and owns the segment and never waits on a reader; N
// readers map it read-only and read the newest published slot tear-free via a
// per-slot seqlock over a triple- (or wider) slot ring. Reads are ZERO-COPY:
// LatestFrameReader::read_latest_into() hands the caller a pointer directly into
// the published slot and validates the seqlock around the caller's consume — no
// per-frame copy and no per-frame allocation (zerobuffer perf rule #1). Caps
// travel in the header as a length-prefixed JSON block consumed verbatim by
// native-player's ShmCaps::parse (4-byte little-endian length prefix + JSON).

#include "zerobuffer/platform.h"
#include "zerobuffer/reader.h"  // ZeroBufferException

#include <atomic>
#include <chrono>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <thread>
#include <vector>

namespace zerobuffer {

// ----- on-segment layout (one definition shared by writer and reader) -------

constexpr uint32_t LATEST_FRAME_MAGIC = 0x3146524Cu;      // 'LFR1'
constexpr uint32_t LATEST_FRAME_VERSION = 1u;
constexpr uint32_t LATEST_FRAME_MIN_SLOTS = 3u;           // triple-slot minimum
constexpr size_t LATEST_FRAME_ALIGNMENT = 64u;            // cache-line alignment
constexpr size_t LATEST_FRAME_METADATA_CAPACITY = 8192u;  // caps JSON block bytes
constexpr int LATEST_FRAME_READ_RETRIES = 16;             // seqlock retries per read
constexpr std::chrono::microseconds LATEST_FRAME_READ_POLL{300};  // poll gap while waiting

// Per-slot header. `seqlock` is the tear-free guard: even = stable, odd = write
// in progress. `frame_number` and `size` are plain fields protected by it.
struct LatestFrameSlotHeader {
    uint64_t seqlock;
    uint64_t frame_number;
    uint64_t size;
    uint64_t reserved;
};
static_assert(sizeof(LatestFrameSlotHeader) == 32, "slot header must be 32 bytes");

// Segment header. POD; accessed cross-process. Concurrency-sensitive fields
// (seqlock, publish_index, metadata_seq, magic) are read/written through
// std::atomic_ref so the underlying bytes stay a plain cross-language layout.
struct LatestFrameSharedHeader {
    uint32_t magic;
    uint32_t version;
    uint32_t slot_count;
    uint32_t slot_header_size;   // sizeof(LatestFrameSlotHeader)
    uint64_t slot_size;          // payload bytes per slot
    uint64_t slot_stride;        // aligned(slot_header_size + slot_size)
    uint64_t slots_offset;       // byte offset of slot 0 from segment start
    uint64_t metadata_offset;    // byte offset of the caps block
    uint64_t metadata_capacity;  // reserved bytes for [u32 len][json]
    uint32_t metadata_seq;       // seqlock over the caps block (even = stable)
    uint32_t reserved0;
    int64_t  publish_index;      // newest published slot; -1 = none published
    uint64_t writer_pid;         // owning writer pid (0 = none)
    uint64_t writer_start_time;  // writer process start time (pid-reuse guard)
    uint64_t reserved1[5];
};
static_assert(sizeof(LatestFrameSharedHeader) == 128, "shared header must be 128 bytes");
static_assert(sizeof(LatestFrameSharedHeader) % LATEST_FRAME_ALIGNMENT == 0,
              "shared header must be cache-line aligned");

// ----- writer (producer) ----------------------------------------------------

// Creates and OWNS the segment. Constructs successfully with no reader present
// and never blocks on or waits for a reader. Reclaims a stale segment left by a
// dead writer on create. Unlinks the segment on destruction.
class LatestFrameWriter {
public:
    // slot_count defaults to the triple-slot minimum; values below it are
    // raised to LATEST_FRAME_MIN_SLOTS. slot_size is the max payload per frame.
    LatestFrameWriter(std::string name, size_t slot_size,
                      uint32_t slot_count = LATEST_FRAME_MIN_SLOTS);
    ~LatestFrameWriter();

    LatestFrameWriter(const LatestFrameWriter&) = delete;
    LatestFrameWriter& operator=(const LatestFrameWriter&) = delete;
    LatestFrameWriter(LatestFrameWriter&&) noexcept;
    LatestFrameWriter& operator=(LatestFrameWriter&&) noexcept;

    // Write the length-prefixed caps block (4-byte LE length + JSON) into the
    // header. Rewritable in place on a caps change (FR-3).
    void set_metadata(const void* caps, size_t len);

    // Reserve the next writable slot (never the just-published slot) and mark it
    // as being written. Returns a pointer to its payload region; the caller
    // writes up to slot_size() bytes then calls publish().
    uint8_t* get_slot();

    // Finalize the slot reserved by get_slot(): record (sequence, size) and make
    // it the newest published slot (seqlock release).
    // `size` above slot_size() is clamped. Never blocks on a reader.
    void publish(uint64_t sequence, size_t size);

    size_t slot_size() const { return _slot_size; }
    uint32_t slot_count() const { return _slot_count; }
    const std::string& name() const { return _name; }

private:
    void reclaim_stale_or_throw();
    void release();
    LatestFrameSlotHeader* slot_hdr(uint32_t i);
    uint8_t* slot_payload(uint32_t i);

    std::string _name;
    size_t _slot_size = 0;
    uint32_t _slot_count = 0;
    std::unique_ptr<SharedMemory> _shm;
    LatestFrameSharedHeader* _header = nullptr;
    uint8_t* _base = nullptr;
    uint32_t _write_index = 0;
    int _pending_slot = -1;      // slot reserved by get_slot(), awaiting publish
    uint64_t _pending_seq_even = 0;  // seqlock value publish() must store
};

// ----- reader (consumer) ----------------------------------------------------

// Maps the segment read-only. Tolerates the segment being absent (the caller
// polls / retries). Never blocks or signals the writer. Supports 1..N readers.
class LatestFrameReader {
public:
    explicit LatestFrameReader(std::string name);
    ~LatestFrameReader();

    LatestFrameReader(const LatestFrameReader&) = delete;
    LatestFrameReader& operator=(const LatestFrameReader&) = delete;
    LatestFrameReader(LatestFrameReader&&) noexcept;
    LatestFrameReader& operator=(LatestFrameReader&&) noexcept;

    // Zero-copy tear-free read of the newest published frame. Invokes
    //   consume(const uint8_t* src, size_t size, uint64_t sequence)
    // with `src` pointing DIRECTLY into the published slot (no copy). The seqlock
    // is validated AROUND the consume, so `consume` must obey a stricter contract
    // than a copy API:
    //   1. it MAY be handed torn bytes (a writer lap mid-read) — do not trust the
    //      pixel values until this function returns true;
    //   2. it MAY be invoked more than once per call (one per retry) — keep the
    //      work restartable/idempotent (e.g. repack into the same target slot);
    //   3. commit / present only when this returns true.
    // Returns false if the segment is absent, no NEW frame appears within
    // `timeout`, or all retries tore. Intermediate frames the writer overwrote
    // are drops (sequence gap). `size` is clamped to slot_size. The primitive
    // stays generic — it never copies and knows nothing about the caller's consume.
    template <class Fn>
    bool read_latest_into(Fn&& consume, std::chrono::milliseconds timeout) {
        auto deadline = std::chrono::steady_clock::now() + timeout;
        for (;;) {
            if (!_header) {
                try_attach();
            }
            if (_header) {
                if (std::atomic_ref<uint32_t>(_header->magic).load(std::memory_order_acquire) !=
                    LATEST_FRAME_MAGIC) {
                    detach();  // producer closed / restarted -> re-attach to a fresh segment
                } else {
                    refresh_metadata();
                    if (try_consume_newest(consume)) {
                        return true;
                    }
                }
            }
            if (std::chrono::steady_clock::now() >= deadline) {
                if (_header && writer_gone()) {
                    detach();  // producer gone -> next call re-attaches (restart recovery)
                }
                return false;
            }
            std::this_thread::sleep_for(LATEST_FRAME_READ_POLL);
        }
    }

    // Caps block as [4-byte LE len][JSON], consumed verbatim by ShmCaps::parse.
    const void* get_metadata_raw();
    size_t get_metadata_size();

    // False = producer gone (dead pid / reused pid), for RECONNECTING (FR-9).
    bool is_writer_alive();

    bool is_attached() const { return _header != nullptr; }
    const std::string& name() const { return _name; }

private:
    bool try_attach();
    void detach();
    void refresh_metadata();
    bool writer_gone();

    LatestFrameSlotHeader* slot_hdr(uint32_t i) {
        return reinterpret_cast<LatestFrameSlotHeader*>(
            _base + _header->slots_offset + static_cast<size_t>(i) * _header->slot_stride);
    }
    uint8_t* slot_payload(uint32_t i) {
        return reinterpret_cast<uint8_t*>(slot_hdr(i)) + sizeof(LatestFrameSlotHeader);
    }

    // Seqlock read of the newest published slot, invoking consume zero-copy and
    // re-checking the seqlock; retries onto the newest slot on a mid-read lap.
    template <class Fn>
    bool try_consume_newest(Fn& consume) {
        for (int attempt = 0; attempt < LATEST_FRAME_READ_RETRIES; ++attempt) {
            int64_t idx =
                std::atomic_ref<int64_t>(_header->publish_index).load(std::memory_order_acquire);
            if (idx < 0 || idx >= static_cast<int64_t>(_header->slot_count)) {
                return false;  // nothing published yet
            }
            uint32_t w = static_cast<uint32_t>(idx);
            LatestFrameSlotHeader* sh = slot_hdr(w);

            uint64_t s1 = std::atomic_ref<uint64_t>(sh->seqlock).load(std::memory_order_acquire);
            if (s1 & 1) {
                std::this_thread::yield();  // writer mid-write on this slot
                continue;
            }
            uint64_t fn = std::atomic_ref<uint64_t>(sh->frame_number).load(std::memory_order_relaxed);
            uint64_t sz = std::atomic_ref<uint64_t>(sh->size).load(std::memory_order_relaxed);
            if (sz > _header->slot_size) {
                sz = _header->slot_size;  // bounds clamp: never hand out past the slot
            }
            if (_have_last && fn == _last_sequence) {
                return false;  // no new frame since the last read
            }

            // Zero-copy: hand the slot payload straight to the caller.
            consume(slot_payload(w), static_cast<size_t>(sz), fn);

            std::atomic_thread_fence(std::memory_order_acquire);
            uint64_t s2 = std::atomic_ref<uint64_t>(sh->seqlock).load(std::memory_order_acquire);
            if (s1 != s2) {
                continue;  // slot rewritten under us -> redo consume on the newest
            }

            _last_sequence = fn;
            _have_last = true;
            return true;
        }
        return false;
    }

    std::string _name;
    std::unique_ptr<SharedMemory> _shm;
    LatestFrameSharedHeader* _header = nullptr;
    uint8_t* _base = nullptr;
    std::vector<uint8_t> _meta_cache;  // cached [u32 len][json] (on-change only)
    size_t _meta_size = 0;
    uint32_t _meta_seq_seen = 0;
    bool _meta_valid = false;
    uint64_t _last_sequence = 0;
    bool _have_last = false;
};

} // namespace zerobuffer

#endif // ZEROBUFFER_LATEST_FRAME_H
