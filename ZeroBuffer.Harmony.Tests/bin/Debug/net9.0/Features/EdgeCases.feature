Feature: Edge Cases and Boundary Conditions
    Tests for edge cases, minimum/maximum values, and boundary conditions
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 11.3 - Zero-Sized Metadata Block
        Given the reader is 'csharp'
        And create buffer 'test-zero-metadata' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-zero-metadata'
        And attempt to write metadata
        
        Then metadata write should fail appropriately
        
        When write frame without metadata
        
        Then frame write should succeed
        And system should work correctly without metadata
        
    Scenario: Test 11.4 - Minimum Buffer Sizes
        Given the reader is 'csharp'
        And create buffer 'test-minimum' with minimum viable size '17'
        
        When the writer is 'python'
        And connect to buffer 'test-minimum'
        And write single byte frame
        
        Then write should succeed
        
        When attempt to write '2' byte frame
        
        Then writer should block waiting for space
        
    Scenario: Test 5.5 - Wrap-Around With Wasted Space
        Given the reader is 'csharp'
        And create buffer 'test-waste' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-waste'
        And write frame that leaves '100' bytes at end
        And attempt to write '200' byte frame
        
        Then writer should write wrap marker at current position
        And payload_free_bytes should be reduced by wasted space
        And frame should be written at buffer start
        
        When the reader is 'csharp'
        And read next frame
        
        Then reader should detect wrap marker
        And reader should jump to buffer start
        And read '200' byte frame successfully
        
    Scenario: Test 5.6 - Continuous Free Space Calculation
        Given the reader is 'csharp'
        And create buffer 'test-free-space' with specific configuration
        
        When test continuous_free_bytes calculation with:
        | write_pos | read_pos | expected_result | scenario |
        | 5000 | 2000 | calculated | write_pos > read_pos |
        | 2000 | 5000 | calculated | write_pos < read_pos |
        | 5000 | 5000 | calculated | write_pos == read_pos empty |
        | 0 | 0 | calculated | both at start |
        | 5000 | 0 | calculated | read_pos at start cannot wrap |
        
        Then calculations should match specification
        
    Scenario: Test 5.7 - Maximum Frame Size
        Given the reader is 'csharp'
        And create buffer 'test-max-frame' with metadata size '0' and payload size '104857600'
        
        When the writer is 'python'
        And connect to buffer 'test-max-frame'
        And write frame matching exactly payload size minus header
        
        Then frame should be written successfully
        
        When attempt to write frame exceeding payload size
        
        Then write should be rejected with appropriate error
        
    Scenario: Test 11.5 - Reader Slower Than Writer
        Given the reader is 'csharp'
        And create buffer 'test-reader-slower' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-reader-slower'
        And write continuously at high speed
        
        And the reader is 'csharp'
        And process with '10' ms delay per frame
        And run for '1000' frames
        
        Then no frames should be lost
        And writer should block appropriately
        And flow control should work correctly
        
    Scenario: Test 13.1 - Protocol Compliance OIEB
        Given the reader is 'csharp'
        And create buffer 'test-oieb-compliance' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-oieb-compliance'
        And perform multiple write operations
        
        Then after each write verify:
        | field | condition |
        | payload_written_count | increments by 1 |
        | payload_free_bytes | decreases by frame size |
        | payload_write_pos | advances correctly |
        | all values | are 64-byte aligned |
        
        When the reader is 'csharp'
        And perform multiple read operations
        
        Then after each read verify:
        | field | condition |
        | payload_read_count | increments by 1 |
        | payload_free_bytes | increases by frame size |
        | payload_read_pos | advances correctly |
        
    Scenario: Test 13.2 - Memory Alignment Verification
        Given the reader is 'csharp'
        And create buffer 'test-alignment' with default config
        
        Then verify OIEB starts at 64-byte aligned address
        And verify metadata block starts at 64-byte aligned offset
        And verify payload block starts at 64-byte aligned offset
        
        When the writer is 'python'
        And connect to buffer 'test-alignment'
        And write various sized frames
        
        Then verify all data access respects alignment