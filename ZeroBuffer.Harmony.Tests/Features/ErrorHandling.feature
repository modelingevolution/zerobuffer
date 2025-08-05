Feature: Error Handling and Recovery Tests
    Tests for error conditions, corruption detection, and recovery scenarios
    
    Background:
        Given the test mode is configured
        
    Scenario: Test 4.1 - Metadata Write-Once Enforcement
        Given the reader is 'csharp'
        And create buffer 'test-metadata-once' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-metadata-once'
        And write metadata with size '500'
        And write frame with data 'test'
        And attempt to write metadata again with size '200'
        
        Then second metadata write should fail
        And original metadata should remain unchanged
        
    Scenario: Test 4.2 - Metadata Size Overflow
        Given the reader is 'csharp'
        And create buffer 'test-metadata-overflow' with metadata size '1024' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-metadata-overflow'
        And attempt to write metadata with size '2048'
        
        Then metadata write should fail with size error
        
    Scenario: Test 5.1 - Corrupted OIEB Detection
        Given the reader is 'csharp'
        And create buffer 'test-corrupt-oieb' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-corrupt-oieb'
        And write frame with data 'valid'
        
        Then corrupt OIEB field 'operation_size' to wrong value
        
        When the writer is 'cpp'
        And attempt to connect to buffer 'test-corrupt-oieb'
        
        Then connection should fail with invalid OIEB error
        
    Scenario: Test 5.2 - Invalid Frame Header Detection
        Given the reader is 'csharp'
        And create buffer 'test-invalid-header' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-invalid-header'
        And write frame with data 'test'
        
        Then corrupt frame header 'payload_size' to '0'
        
        When the reader is 'csharp'
        And attempt to read frame
        
        Then read should fail with invalid frame size error
        
    Scenario: Test 5.3 - Reader Death During Write
        Given the reader is 'csharp'
        And create buffer 'test-reader-death' with metadata size '0' and payload size '10240'
        
        When the writer is 'python'
        And connect to buffer 'test-reader-death'
        And start writing large frame of '8192' bytes
        
        Then the reader is 'csharp'
        And simulate crash while write in progress
        
        When the writer is 'python'
        And complete write operation
        And attempt next operation
        
        Then writer should detect reader death
        And throw reader dead exception
        
    Scenario: Test 6.1 - Sequence Number Gap Detection
        Given the reader is 'csharp'
        And create buffer 'test-sequence-gap' with default config
        
        When the writer is 'python'
        And connect to buffer 'test-sequence-gap'
        And write frame with sequence '1'
        And write frame with sequence '2'
        And write frame with sequence '3'
        
        Then the reader is 'csharp'
        And read frame with sequence '1'
        And read frame with sequence '2'
        
        When corrupt next frame sequence to '5'
        And attempt to read frame
        
        Then read should fail with sequence error
        And error should show expected '4' got '5'
        
    Scenario: Test 7.1 - Partial Initialization Failure
        Given attempt to create buffer 'test-partial-init' with default config
        And simulate semaphore creation failure
        
        Then buffer creation should fail
        And shared memory should be cleaned up
        And no resources should be leaked
        
    Scenario: Test 7.2 - System Resource Exhaustion
        Given create maximum allowed shared memory segments
        
        When attempt to create one more buffer 'test-exhausted'
        
        Then creation should fail with system error
        And appropriate error message should be returned
        
    Scenario: Test 8.1 - Stale Resource Cleanup
        Given manually create stale lock file for 'stale-test'
        And create orphaned shared memory for 'stale-test'
        And create orphaned semaphores for 'stale-test'
        
        When the reader is 'csharp'
        And attempt to create buffer 'stale-test'
        
        Then stale resources should be detected
        And old resources should be cleaned up
        And new buffer should be created successfully