#include "zerobuffer/latest_frame.h"

#include <atomic>
#include <chrono>
#include <cstring>
#include <thread>
#include <utility>

// Producer-owned latest-frame primitive (ADR-11). The seqlock lives in each
// slot's `seqlock` field: the writer makes it odd before touching the payload
// and even after, so a reader that sees an even value both before and after its
// copy is guaranteed a tear-free frame. The triple- (or wider) slot ring means
// the slot a reader is copying is not the writer's next target, so a lap during
// a read is rare; the reader retries and lands on the newest slot.
//
// Cross-process atomicity uses std::atomic_ref over plain POD fields so the
// on-segment bytes stay a language-neutral layout. Ordering:
//   - writer: payload store, then seqlock even (release), then publish_index
//     (release) -> a reader that acquires publish_index sees the payload.
//   - reader: publish_index (acquire), seqlock (acquire), copy, acquire fence,
//     seqlock re-check.

namespace zerobuffer {
namespace {

constexpr int MAX_READ_RETRIES = 16;
constexpr auto READ_POLL_INTERVAL = std::chrono::microseconds(300);

// A completed metadata write leaves metadata_seq even and >= 2 (0 = never
// written, 1 = first write in progress). Readers treat < 2 as "no caps yet".
constexpr uint32_t METADATA_WRITTEN = 2;

size_t align_up(size_t v, size_t a) {
    return (v + a - 1) & ~(a - 1);
}

// Force `seqlock` to the next odd value strictly greater than its current one,
// so a slot left odd by a dropped write still advances monotonically.
uint64_t begin_write_value(uint64_t current) {
    return (current % 2 == 0) ? (current + 1) : (current + 2);
}

} // namespace

// ----------------------------- Writer ---------------------------------------

LatestFrameWriter::LatestFrameWriter(std::string name, size_t slot_size, uint32_t slot_count)
    : _name(std::move(name)), _slot_size(slot_size) {
    if (_slot_size == 0) {
        throw ZeroBufferException("LatestFrameWriter: slot_size must be non-zero");
    }
    _slot_count = slot_count < LATEST_FRAME_MIN_SLOTS ? LATEST_FRAME_MIN_SLOTS : slot_count;

    const uint64_t slot_stride = align_up(sizeof(LatestFrameSlotHeader) + _slot_size,
                                          LATEST_FRAME_ALIGNMENT);
    const uint64_t metadata_offset = sizeof(LatestFrameSharedHeader);
    const uint64_t slots_offset = align_up(metadata_offset + LATEST_FRAME_METADATA_CAPACITY,
                                           LATEST_FRAME_ALIGNMENT);
    const size_t total = static_cast<size_t>(slots_offset) +
                         static_cast<size_t>(_slot_count) * static_cast<size_t>(slot_stride);

    try {
        _shm = SharedMemory::create(_name, total);
    } catch (const ZeroBufferException&) {
        reclaim_stale_or_throw();
        _shm = SharedMemory::create(_name, total);  // propagates on genuine failure
    }

    _base = static_cast<uint8_t*>(_shm->data());
    _header = reinterpret_cast<LatestFrameSharedHeader*>(_base);

    // Segment is zeroed by create(); fill everything but magic, then publish the
    // magic with release so a reader only attaches once the layout is valid.
    _header->version = LATEST_FRAME_VERSION;
    _header->slot_count = _slot_count;
    _header->slot_header_size = static_cast<uint32_t>(sizeof(LatestFrameSlotHeader));
    _header->slot_size = _slot_size;
    _header->slot_stride = slot_stride;
    _header->slots_offset = slots_offset;
    _header->metadata_offset = metadata_offset;
    _header->metadata_capacity = LATEST_FRAME_METADATA_CAPACITY;
    _header->metadata_seq = 0;
    _header->publish_index = -1;
    _header->writer_pid = platform::get_current_pid();
    _header->writer_start_time = platform::get_current_process_start_time();

    std::atomic_ref<uint32_t>(_header->magic).store(LATEST_FRAME_MAGIC, std::memory_order_release);
}

LatestFrameWriter::~LatestFrameWriter() {
    release();
}

LatestFrameWriter::LatestFrameWriter(LatestFrameWriter&& o) noexcept
    : _name(std::move(o._name)), _slot_size(o._slot_size), _slot_count(o._slot_count),
      _shm(std::move(o._shm)), _header(o._header), _base(o._base),
      _write_index(o._write_index), _pending_slot(o._pending_slot),
      _pending_seq_even(o._pending_seq_even) {
    o._header = nullptr;
    o._base = nullptr;
    o._pending_slot = -1;
}

LatestFrameWriter& LatestFrameWriter::operator=(LatestFrameWriter&& o) noexcept {
    if (this != &o) {
        release();
        _name = std::move(o._name);
        _slot_size = o._slot_size;
        _slot_count = o._slot_count;
        _shm = std::move(o._shm);
        _header = o._header;
        _base = o._base;
        _write_index = o._write_index;
        _pending_slot = o._pending_slot;
        _pending_seq_even = o._pending_seq_even;
        o._header = nullptr;
        o._base = nullptr;
        o._pending_slot = -1;
    }
    return *this;
}

void LatestFrameWriter::reclaim_stale_or_throw() {
    std::unique_ptr<SharedMemory> shm;
    try {
        shm = SharedMemory::open(_name);
    } catch (const ZeroBufferException&) {
        // Present but unmappable (e.g. a partial segment from a writer that died
        // mid-create): clear it so the retry create() can own the name.
        SharedMemory::remove(_name);
        return;
    }

    if (!shm || shm->size() < sizeof(LatestFrameSharedHeader)) {
        SharedMemory::remove(_name);
        return;
    }

    auto* h = static_cast<LatestFrameSharedHeader*>(shm->data());
    uint32_t magic = std::atomic_ref<uint32_t>(h->magic).load(std::memory_order_acquire);
    if (magic == LATEST_FRAME_MAGIC) {
        uint64_t pid = h->writer_pid;
        uint64_t start = h->writer_start_time;
        bool alive = pid != 0 && platform::process_exists(pid) &&
                     (start == 0 || platform::get_process_start_time(pid) == start);
        if (alive) {
            throw ZeroBufferException("LatestFrameWriter: segment '" + _name +
                                      "' is owned by a live writer (pid " +
                                      std::to_string(pid) + ")");
        }
    }
    // Stale (dead writer) or foreign/uninitialized content -> reclaim the name.
    shm.reset();
    SharedMemory::remove(_name);
}

void LatestFrameWriter::release() {
    if (_shm && _header) {
        // Invalidate the magic in the shared mapping BEFORE unlinking so any
        // reader sharing these pages sees "producer closed" immediately and
        // re-attaches to a fresh segment (works even in-process, where pid
        // liveness cannot distinguish a restart). Then unlink the name.
        std::atomic_ref<uint32_t>(_header->magic).store(0, std::memory_order_release);
        SharedMemory::remove(_name);
    }
    _shm.reset();
    _header = nullptr;
    _base = nullptr;
}

LatestFrameSlotHeader* LatestFrameWriter::slot_hdr(uint32_t i) {
    return reinterpret_cast<LatestFrameSlotHeader*>(_base + _header->slots_offset +
                                                    static_cast<size_t>(i) * _header->slot_stride);
}

uint8_t* LatestFrameWriter::slot_payload(uint32_t i) {
    return reinterpret_cast<uint8_t*>(slot_hdr(i)) + sizeof(LatestFrameSlotHeader);
}

void LatestFrameWriter::set_metadata(const void* caps, size_t len) {
    if (4 + len > _header->metadata_capacity) {
        throw ZeroBufferException("LatestFrameWriter: caps metadata (" + std::to_string(len) +
                                  " bytes) exceeds capacity");
    }
    uint8_t* mbase = _base + _header->metadata_offset;
    std::atomic_ref<uint32_t> mseq(_header->metadata_seq);
    uint64_t begin = begin_write_value(mseq.load(std::memory_order_relaxed));
    mseq.store(static_cast<uint32_t>(begin), std::memory_order_release);
    std::atomic_thread_fence(std::memory_order_release);

    uint32_t l = static_cast<uint32_t>(len);
    mbase[0] = static_cast<uint8_t>(l & 0xFF);
    mbase[1] = static_cast<uint8_t>((l >> 8) & 0xFF);
    mbase[2] = static_cast<uint8_t>((l >> 16) & 0xFF);
    mbase[3] = static_cast<uint8_t>((l >> 24) & 0xFF);
    if (len > 0) {
        std::memcpy(mbase + 4, caps, len);
    }

    std::atomic_thread_fence(std::memory_order_release);
    mseq.store(static_cast<uint32_t>(begin + 1), std::memory_order_release);
}

uint8_t* LatestFrameWriter::get_slot() {
    int64_t pub = std::atomic_ref<int64_t>(_header->publish_index).load(std::memory_order_acquire);
    uint32_t w = (_write_index + 1) % _slot_count;
    if (static_cast<int64_t>(w) == pub) {
        w = (w + 1) % _slot_count;
    }
    _write_index = w;
    _pending_slot = static_cast<int>(w);

    LatestFrameSlotHeader* sh = slot_hdr(w);
    std::atomic_ref<uint64_t> sref(sh->seqlock);
    uint64_t begin = begin_write_value(sref.load(std::memory_order_relaxed));
    sref.store(begin, std::memory_order_release);
    _pending_seq_even = begin + 1;
    std::atomic_thread_fence(std::memory_order_release);
    return slot_payload(w);
}

void LatestFrameWriter::publish(uint64_t sequence, size_t size) {
    if (_pending_slot < 0) {
        return;
    }
    uint32_t w = static_cast<uint32_t>(_pending_slot);
    if (size > _slot_size) {
        size = _slot_size;
    }
    LatestFrameSlotHeader* sh = slot_hdr(w);
    // Relaxed atomic stores of the seqlock-guarded fields: the seqlock release
    // below is the actual publication fence; relaxed keeps these free of any
    // abstract-machine data race on the fields themselves.
    std::atomic_ref<uint64_t>(sh->frame_number).store(sequence, std::memory_order_relaxed);
    std::atomic_ref<uint64_t>(sh->size).store(size, std::memory_order_relaxed);

    std::atomic_thread_fence(std::memory_order_release);
    std::atomic_ref<uint64_t>(sh->seqlock).store(_pending_seq_even, std::memory_order_release);
    std::atomic_ref<int64_t>(_header->publish_index).store(static_cast<int64_t>(w),
                                                           std::memory_order_release);
    _pending_slot = -1;
}

// ----------------------------- Reader ---------------------------------------

LatestFrameReader::LatestFrameReader(std::string name) : _name(std::move(name)) {
    try_attach();
}

LatestFrameReader::~LatestFrameReader() {
    detach();
}

LatestFrameReader::LatestFrameReader(LatestFrameReader&& o) noexcept
    : _name(std::move(o._name)), _shm(std::move(o._shm)), _header(o._header), _base(o._base),
      _scratch(std::move(o._scratch)), _meta_cache(std::move(o._meta_cache)),
      _meta_size(o._meta_size), _meta_seq_seen(o._meta_seq_seen), _meta_valid(o._meta_valid),
      _last_sequence(o._last_sequence), _have_last(o._have_last) {
    o._header = nullptr;
    o._base = nullptr;
}

LatestFrameReader& LatestFrameReader::operator=(LatestFrameReader&& o) noexcept {
    if (this != &o) {
        detach();
        _name = std::move(o._name);
        _shm = std::move(o._shm);
        _header = o._header;
        _base = o._base;
        _scratch = std::move(o._scratch);
        _meta_cache = std::move(o._meta_cache);
        _meta_size = o._meta_size;
        _meta_seq_seen = o._meta_seq_seen;
        _meta_valid = o._meta_valid;
        _last_sequence = o._last_sequence;
        _have_last = o._have_last;
        o._header = nullptr;
        o._base = nullptr;
    }
    return *this;
}

bool LatestFrameReader::try_attach() {
    if (_header) {
        return true;
    }
    std::unique_ptr<SharedMemory> shm;
    try {
        shm = SharedMemory::open(_name);
    } catch (const ZeroBufferException&) {
        return false;  // absent -> caller polls
    }
    if (!shm || shm->size() < sizeof(LatestFrameSharedHeader)) {
        return false;
    }

    auto* h = static_cast<LatestFrameSharedHeader*>(shm->data());
    if (std::atomic_ref<uint32_t>(h->magic).load(std::memory_order_acquire) != LATEST_FRAME_MAGIC) {
        return false;  // creator has not published the layout yet
    }
    if (h->version != LATEST_FRAME_VERSION || h->slot_count == 0 || h->slot_size == 0) {
        return false;
    }
    uint64_t need = h->slots_offset + static_cast<uint64_t>(h->slot_count) * h->slot_stride;
    if (need > shm->size()) {
        return false;
    }

    _shm = std::move(shm);
    _header = h;
    _base = static_cast<uint8_t*>(_shm->data());
    _scratch.assign(static_cast<size_t>(_header->slot_size), 0);
    _meta_valid = false;
    _meta_size = 0;
    _meta_seq_seen = 0;
    _have_last = false;
    refresh_metadata();
    return true;
}

void LatestFrameReader::detach() {
    _shm.reset();
    _header = nullptr;
    _base = nullptr;
    _meta_valid = false;
    _have_last = false;
}

const LatestFrameSlotHeader* LatestFrameReader::slot_hdr(uint32_t i) const {
    return reinterpret_cast<const LatestFrameSlotHeader*>(
        _base + _header->slots_offset + static_cast<size_t>(i) * _header->slot_stride);
}

const uint8_t* LatestFrameReader::slot_payload(uint32_t i) const {
    return reinterpret_cast<const uint8_t*>(slot_hdr(i)) + sizeof(LatestFrameSlotHeader);
}

bool LatestFrameReader::try_read_once(LatestFrame& out) {
    for (int attempt = 0; attempt < MAX_READ_RETRIES; ++attempt) {
        int64_t idx =
            std::atomic_ref<int64_t>(_header->publish_index).load(std::memory_order_acquire);
        if (idx < 0 || idx >= static_cast<int64_t>(_header->slot_count)) {
            return false;  // nothing published yet
        }
        uint32_t w = static_cast<uint32_t>(idx);
        const LatestFrameSlotHeader* sh = slot_hdr(w);
        uint64_t& seq_field = const_cast<uint64_t&>(sh->seqlock);

        uint64_t s1 = std::atomic_ref<uint64_t>(seq_field).load(std::memory_order_acquire);
        if (s1 & 1) {
            std::this_thread::yield();  // writer mid-write on this slot
            continue;
        }
        uint64_t fn = std::atomic_ref<uint64_t>(const_cast<uint64_t&>(sh->frame_number))
                          .load(std::memory_order_relaxed);
        uint64_t sz = std::atomic_ref<uint64_t>(const_cast<uint64_t&>(sh->size))
                          .load(std::memory_order_relaxed);
        if (sz > _scratch.size()) {
            sz = _scratch.size();  // defensive: never read past the slot
        }
        // Plain payload copy, validated by the seqlock re-check below: a byte
        // race here is torn iff the seqlock changed, which the re-check catches.
        std::memcpy(_scratch.data(), slot_payload(w), sz);
        std::atomic_thread_fence(std::memory_order_acquire);

        uint64_t s2 = std::atomic_ref<uint64_t>(seq_field).load(std::memory_order_acquire);
        if (s1 != s2) {
            continue;  // slot rewritten under us -> retry, lands on the newest
        }

        if (_have_last && fn == _last_sequence) {
            return false;  // no new frame since the last read
        }
        _last_sequence = fn;
        _have_last = true;
        out._data = _scratch.data();
        out._size = static_cast<size_t>(sz);
        out._sequence = fn;
        out._valid = true;
        return true;
    }
    return false;
}

LatestFrame LatestFrameReader::read_latest(std::chrono::milliseconds timeout) {
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
                LatestFrame f;
                if (try_read_once(f)) {
                    return f;
                }
            }
        }
        if (std::chrono::steady_clock::now() >= deadline) {
            // Producer gone: drop the mapping so the next call re-attaches to a
            // freshly created segment (writer restart recovery, ADR-5).
            if (_header && writer_gone()) {
                detach();
            }
            return LatestFrame{};
        }
        std::this_thread::sleep_for(READ_POLL_INTERVAL);
    }
}

void LatestFrameReader::refresh_metadata() {
    if (!_header) {
        return;
    }
    std::atomic_ref<uint32_t> mseq(_header->metadata_seq);
    uint32_t s1 = mseq.load(std::memory_order_acquire);
    if (s1 < METADATA_WRITTEN) {
        return;  // no caps written yet -> stays "not available", not empty caps
    }
    if (s1 & 1) {
        return;  // caps being rewritten -> keep the previous cache
    }
    if (_meta_valid && s1 == _meta_seq_seen) {
        return;  // unchanged
    }

    const uint8_t* mbase = _base + _header->metadata_offset;
    uint32_t len = static_cast<uint32_t>(mbase[0]) |
                   (static_cast<uint32_t>(mbase[1]) << 8) |
                   (static_cast<uint32_t>(mbase[2]) << 16) |
                   (static_cast<uint32_t>(mbase[3]) << 24);
    if (4 + static_cast<uint64_t>(len) > _header->metadata_capacity) {
        return;  // malformed -> keep the previous cache
    }
    _meta_cache.resize(4 + static_cast<size_t>(len));
    std::memcpy(_meta_cache.data(), mbase, _meta_cache.size());
    std::atomic_thread_fence(std::memory_order_acquire);

    uint32_t s2 = mseq.load(std::memory_order_acquire);
    if (s1 != s2) {
        return;  // torn -> refresh next time
    }
    _meta_seq_seen = s1;
    _meta_size = _meta_cache.size();
    _meta_valid = true;
}

const void* LatestFrameReader::get_metadata_raw() {
    return _meta_valid ? _meta_cache.data() : nullptr;
}

size_t LatestFrameReader::get_metadata_size() {
    return _meta_valid ? _meta_size : 0;
}

bool LatestFrameReader::is_writer_alive() {
    if (!_header) {
        return false;
    }
    if (std::atomic_ref<uint32_t>(_header->magic).load(std::memory_order_acquire) !=
        LATEST_FRAME_MAGIC) {
        return false;  // producer closed the segment
    }
    uint64_t pid = _header->writer_pid;
    if (pid == 0 || !platform::process_exists(pid)) {
        return false;
    }
    uint64_t start = _header->writer_start_time;
    if (start != 0 && platform::get_process_start_time(pid) != start) {
        return false;  // pid reused by a different process
    }
    return true;
}

bool LatestFrameReader::writer_gone() {
    return !is_writer_alive();
}

} // namespace zerobuffer
