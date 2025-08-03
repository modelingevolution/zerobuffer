#ifndef ZEROBUFFER_READER_IMPL_H
#define ZEROBUFFER_READER_IMPL_H

#include "zerobuffer/reader.h"

namespace zerobuffer {

// Template implementation
template<typename T>
inline const T* Reader::get_metadata_ptr() const {
    // We need access to impl's internals, so we'll use a different approach
    size_t meta_size = get_metadata_size();
    if (meta_size < sizeof(T)) {
        return nullptr;
    }
    
    // Get metadata as bytes
    auto metadata = get_metadata();
    if (metadata.empty()) {
        return nullptr;
    }
    
    // This is not zero-copy yet, we need a better approach
    // For now, return nullptr
    return nullptr;
}

} // namespace zerobuffer

#endif // ZEROBUFFER_READER_IMPL_H