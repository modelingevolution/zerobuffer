#ifndef ZEROBUFFER_LATEST_FRAME_H
#define ZEROBUFFER_LATEST_FRAME_H

// Producer-owned, consumer-optional, newest-frame-wins display buffer
// (Epic 020 / feature-002, ADR-1 + ADR-11). Purely additive: built on
// zerobuffer's platform layer (SharedMemory / process_exists) and does NOT
// touch the SPSC Reader/Writer, so the AI path's SPSC contract is unchanged.
//
// The writer creates and owns the segment and never waits on a reader; N
// readers map it read-only and read the newest published slot tear-free via a
// per-slot seqlock over a triple- (or wider) slot ring. Caps travel in the
// header as a length-prefixed JSON block consumed verbatim by native-player's
// ShmCaps::parse (4-byte little-endian length prefix + JSON).

#include "zerobuffer/platform.h"
#include "zerobuffer/reader.h"  // ZeroBufferException

#include <chrono>
#include <cstddef>
#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace zerobuffer {

// ----- on-segment layout (one definition shared by writer and reader) -------

constexpr uint32_t LATEST_FRAME_MAGIC = 0x3146524Cu;      // 'LFR1'
constexpr uint32_t LATEST_FRAME_VERSION = 1u;
constexpr uint32_t LATEST_FRAME_MIN_SLOTS = 3u;           // triple-slot minimum
constexpr size_t LATEST_FRAME_ALIGNMENT = 64u;            // cache-line alignment
constexpr size_t LATEST_FRAME_METADATA_CAPACITY = 8192u;  // caps JSON block bytes

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

// ----- frame view returned by the reader ------------------------------------

// Zero-allocation view of one tear-free frame copy. `data()` points into the
// reader's reusable scratch buffer and is valid until the next read_latest().
class LatestFrame {
public:
    LatestFrame() = default;

    const void* data() const { return _data; }
    size_t size() const { return _size; }
    uint64_t sequence() const { return _sequence; }
    bool valid() const { return _valid; }

private:
    friend class LatestFrameReader;
    const void* _data = nullptr;
    size_t _size = 0;
    uint64_t _sequence = 0;
    bool _valid = false;
};

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

    // Read the newest published frame tear-free (seqlock). Returns an invalid
    // frame if the segment is absent or no NEW frame appears within `timeout`.
    // Intermediate frames the writer overwrote are drops (sequence gap).
    LatestFrame read_latest(std::chrono::milliseconds timeout);

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
    bool try_read_once(LatestFrame& out);
    void refresh_metadata();
    bool writer_gone();
    const LatestFrameSlotHeader* slot_hdr(uint32_t i) const;
    const uint8_t* slot_payload(uint32_t i) const;

    std::string _name;
    std::unique_ptr<SharedMemory> _shm;
    LatestFrameSharedHeader* _header = nullptr;
    uint8_t* _base = nullptr;
    std::vector<uint8_t> _scratch;     // reused frame copy (no per-frame alloc)
    std::vector<uint8_t> _meta_cache;  // cached [u32 len][json]
    size_t _meta_size = 0;
    uint32_t _meta_seq_seen = 0;
    bool _meta_valid = false;
    uint64_t _last_sequence = 0;
    bool _have_last = false;
};

} // namespace zerobuffer

#endif // ZEROBUFFER_LATEST_FRAME_H
