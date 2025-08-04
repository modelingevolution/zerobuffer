# ZeroBuffer Python - Logging Guide

## Overview

The ZeroBuffer Python implementation includes comprehensive logging support similar to C#'s ILogger pattern. The logging system provides detailed insights into buffer operations, helpful for debugging and monitoring.

## Quick Start

### Basic Setup

```python
import zerobuffer

# Configure logging before creating buffers
zerobuffer.setup_logging(level="INFO")

# Create reader/writer - they will log automatically
reader = zerobuffer.Reader("my_buffer")
writer = zerobuffer.Writer("my_buffer")
```

### Custom Logger

```python
import logging
import zerobuffer

# Create custom logger
logger = logging.getLogger("myapp.zerobuffer")
logger.setLevel(logging.DEBUG)

# Pass logger to Reader/Writer
reader = zerobuffer.Reader("my_buffer", logger=logger)
writer = zerobuffer.Writer("my_buffer", logger=logger)
```

## Log Levels

Following the same patterns as C# ILogger:

- **DEBUG**: Detailed state information (OIEB state, positions, etc.)
- **INFO**: Important operations (buffer creation, connections, closing)
- **WARNING**: Potential issues (process death, timeouts)
- **ERROR**: Errors that prevent operations (invalid frames, sequence errors)

## Log Messages

### Reader Log Messages

```
INFO - Creating shared memory: name=buffer, size=1049728 bytes
INFO - Reader created successfully: pid=12345
DEBUG - OIEB state: WrittenCount=0, ReadCount=0, WritePos=0, ReadPos=0
DEBUG - Reading frame: seq=1, size=1024 from position 0
DEBUG - Frame read complete: seq=1, new state: ReadCount=1, FreeBytes=1024
DEBUG - Found wrap marker at position 1048000, handling wrap-around
WARNING - Writer process 12346 is dead
ERROR - Sequence error: expected 5, got 7
INFO - Closing Reader: frames_read=1000, bytes_read=1024000
```

### Writer Log Messages

```
DEBUG - Creating Writer for buffer: buffer_name
INFO - Writer connected successfully: pid=12346
DEBUG - WriteFrame called with data size=1024
DEBUG - Writing frame: seq=1, size=1024 at position 0
DEBUG - Frame written: seq=1, new state: WrittenCount=1, FreeBytes=1047552
DEBUG - Need to wrap: continuous_free=1024, space_to_end=576, total_size=1024
DEBUG - Writing wrap marker at position 1048000
WARNING - Reader process is dead
ERROR - Frame too large: 2097152 > 1048576
INFO - Closing Writer: frames_written=1000, bytes_written=1024000
```

## Configuration Options

### setup_logging()

```python
zerobuffer.setup_logging(
    level="INFO",              # Log level: DEBUG, INFO, WARNING, ERROR, CRITICAL
    log_file="zerobuffer.log", # Optional: write to file
    format_string=None         # Optional: custom format
)
```

### Environment Variables

```bash
# Set log level via environment
export ZEROBUFFER_LOG_LEVEL=DEBUG

# Python code
import os
import zerobuffer

level = os.environ.get('ZEROBUFFER_LOG_LEVEL', 'INFO')
zerobuffer.setup_logging(level=level)
```

## Integration with Application Logging

### Using with existing logging config

```python
import logging.config
import zerobuffer

# Your app's logging config
logging.config.dictConfig({
    'version': 1,
    'handlers': {
        'console': {
            'class': 'logging.StreamHandler',
            'formatter': 'standard'
        }
    },
    'formatters': {
        'standard': {
            'format': '%(asctime)s [%(levelname)s] %(name)s: %(message)s'
        }
    },
    'loggers': {
        'zerobuffer': {
            'level': 'DEBUG',
            'handlers': ['console']
        }
    }
})

# ZeroBuffer will use your configuration
reader = zerobuffer.Reader("buffer")
```

### Filtering by component

```python
# Only show warnings and above for Reader
logging.getLogger('zerobuffer.reader').setLevel(logging.WARNING)

# Debug level for Writer only
logging.getLogger('zerobuffer.writer').setLevel(logging.DEBUG)
```

## Performance Considerations

- Logging at DEBUG level has minimal performance impact
- Use INFO or WARNING in production for best performance
- Structured logging preserves performance while providing rich data

## Best Practices

1. **Enable DEBUG logging during development** to understand buffer behavior
2. **Use INFO level in production** for important events only
3. **Monitor WARNING messages** - they often indicate recoverable issues
4. **Log to files in production** for post-mortem analysis
5. **Use structured logging** for easier parsing and analysis

## Example: Production Setup

```python
import zerobuffer
import logging
from logging.handlers import RotatingFileHandler

def setup_production_logging():
    # Setup rotating file handler
    handler = RotatingFileHandler(
        'zerobuffer.log',
        maxBytes=10*1024*1024,  # 10MB
        backupCount=5
    )
    
    # Configure zerobuffer logging
    zerobuffer.setup_logging(
        level="INFO",
        log_file="zerobuffer.log"
    )
    
    # Also setup console output for errors
    console = logging.StreamHandler()
    console.setLevel(logging.ERROR)
    logging.getLogger('zerobuffer').addHandler(console)

# Use in production
setup_production_logging()
reader = zerobuffer.Reader("prod_buffer")
```

## Troubleshooting with Logs

### Debugging Sequence Errors

Enable DEBUG logging to see the exact sequence numbers:
```
DEBUG - Reading frame: seq=5, size=1024 from position 140
ERROR - Sequence error: expected 6, got 8
```

### Debugging Buffer Full Conditions

```
DEBUG - OIEB state: WrittenCount=100, ReadCount=50, WritePos=51200, ReadPos=25600, FreeBytes=0
DEBUG - Waiting for reader to free space (need 1024 bytes, have 0)
```

### Debugging Wrap-Around Issues

```
DEBUG - Need to wrap: continuous_free=2048, space_to_end=1024, total_size=1536
DEBUG - Writing wrap marker at position 1047552
DEBUG - After wrap: WritePos=0, WrittenCount=101, FreeBytes=1047552
```