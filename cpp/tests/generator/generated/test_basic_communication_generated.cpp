// Generated from: 01-BasicCommunication.feature
// DO NOT EDIT - This file is auto-generated

#include <gtest/gtest.h>
#include <memory>
#include <string>
#include <vector>
#include "step_definitions/step_registry.h"
#include "step_definitions/test_context.h"
#include "step_definitions/basic_communication_steps.h"

using namespace zerobuffer::steps;

class BasicCommunicationTest : public ::testing::Test {
protected:
    StepRegistry& registry = StepRegistry::getInstance();
    TestContext context;

    void SetUp() override {
        // Register step definitions
        registerBasicCommunicationSteps();
        context.reset();
    }

    void TearDown() override {
        // Clean up any resources
        context.reset();
    }

    bool ExecuteStep(const std::string& step) {
        return registry.executeStep(step, context);
    }
};

TEST_F(BasicCommunicationTest, Test_1_1_Simple_Write_Read_Cycle) {
    // Scenario: Test 1.1 - Simple Write-Read Cycle

    // Background steps
    // Given the test environment is initialized
    ASSERT_TRUE(ExecuteStep("the test environment is initialized")) << "Failed: the test environment is initialized";
    // And all processes are ready
    ASSERT_TRUE(ExecuteStep("all processes are ready")) << "Failed: all processes are ready";

    // Scenario steps
    // Given the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'
    ASSERT_TRUE(ExecuteStep("the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'")) << "Failed: the 'reader' process creates buffer 'test-basic' with metadata size '1024' and payload size '10240'";
    // When the 'writer' process connects to buffer 'test-basic'
    ASSERT_TRUE(ExecuteStep("the 'writer' process connects to buffer 'test-basic'")) << "Failed: the 'writer' process connects to buffer 'test-basic'";
    // And the 'writer' process writes metadata with size '100'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes metadata with size '100'")) << "Failed: the 'writer' process writes metadata with size '100'";
    // And the 'writer' process writes frame with size '1024' and sequence '1'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with size '1024' and sequence '1'")) << "Failed: the 'writer' process writes frame with size '1024' and sequence '1'";
    // Then the 'reader' process should read frame with sequence '1' and size '1024'
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read frame with sequence '1' and size '1024'")) << "Failed: the 'reader' process should read frame with sequence '1' and size '1024'";
    // And the 'reader' process should validate frame data
    ASSERT_TRUE(ExecuteStep("the 'reader' process should validate frame data")) << "Failed: the 'reader' process should validate frame data";
    // And the 'reader' process signals space available
    ASSERT_TRUE(ExecuteStep("the 'reader' process signals space available")) << "Failed: the 'reader' process signals space available";
}

TEST_F(BasicCommunicationTest, Test_1_2_Multiple_Frames_Sequential) {
    // Scenario: Test 1.2 - Multiple Frames Sequential

    // Background steps
    // Given the test environment is initialized
    ASSERT_TRUE(ExecuteStep("the test environment is initialized")) << "Failed: the test environment is initialized";
    // And all processes are ready
    ASSERT_TRUE(ExecuteStep("all processes are ready")) << "Failed: all processes are ready";

    // Scenario steps
    // Given the 'reader' process creates buffer 'test-multi' with metadata size '1024' and payload size '102400'
    ASSERT_TRUE(ExecuteStep("the 'reader' process creates buffer 'test-multi' with metadata size '1024' and payload size '102400'")) << "Failed: the 'reader' process creates buffer 'test-multi' with metadata size '1024' and payload size '102400'";
    // When the 'writer' process connects to buffer 'test-multi'
    ASSERT_TRUE(ExecuteStep("the 'writer' process connects to buffer 'test-multi'")) << "Failed: the 'writer' process connects to buffer 'test-multi'";
    // And the 'writer' process writes frame with sequence '1'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with sequence '1'")) << "Failed: the 'writer' process writes frame with sequence '1'";
    // And the 'writer' process writes frame with sequence '2'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with sequence '2'")) << "Failed: the 'writer' process writes frame with sequence '2'";
    // And the 'writer' process writes frame with sequence '3'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with sequence '3'")) << "Failed: the 'writer' process writes frame with sequence '3'";
    // Then the 'reader' process should read frame with sequence '1';
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read frame with sequence '1';")) << "Failed: the 'reader' process should read frame with sequence '1';";
    // And the 'reader' process signals space available
    ASSERT_TRUE(ExecuteStep("the 'reader' process signals space available")) << "Failed: the 'reader' process signals space available";
    // And the 'reader' process should read frame with sequence '2';
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read frame with sequence '2';")) << "Failed: the 'reader' process should read frame with sequence '2';";
    // And the 'reader' process signals space available
    ASSERT_TRUE(ExecuteStep("the 'reader' process signals space available")) << "Failed: the 'reader' process signals space available";
    // And the 'reader' process should read frame with sequence '3';
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read frame with sequence '3';")) << "Failed: the 'reader' process should read frame with sequence '3';";
    // And the 'reader' process should verify all frames maintain sequential order
    ASSERT_TRUE(ExecuteStep("the 'reader' process should verify all frames maintain sequential order")) << "Failed: the 'reader' process should verify all frames maintain sequential order";
}

TEST_F(BasicCommunicationTest, Test_1_3_Buffer_Full_Handling) {
    // Scenario: Test 1.3 - Buffer Full Handling

    // Background steps
    // Given the test environment is initialized
    ASSERT_TRUE(ExecuteStep("the test environment is initialized")) << "Failed: the test environment is initialized";
    // And all processes are ready
    ASSERT_TRUE(ExecuteStep("all processes are ready")) << "Failed: all processes are ready";

    // Scenario steps
    // Given the 'reader' process creates buffer 'test-full' with metadata size '1024' and payload size '10240'
    ASSERT_TRUE(ExecuteStep("the 'reader' process creates buffer 'test-full' with metadata size '1024' and payload size '10240'")) << "Failed: the 'reader' process creates buffer 'test-full' with metadata size '1024' and payload size '10240'";
    // When the 'writer' process connects to buffer 'test-full'
    ASSERT_TRUE(ExecuteStep("the 'writer' process connects to buffer 'test-full'")) << "Failed: the 'writer' process connects to buffer 'test-full'";
    // And the 'writer' process writes frames until buffer is full
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frames until buffer is full")) << "Failed: the 'writer' process writes frames until buffer is full";
    // Then the 'writer' process should experience timeout on next write
    ASSERT_TRUE(ExecuteStep("the 'writer' process should experience timeout on next write")) << "Failed: the 'writer' process should experience timeout on next write";
    // When the 'reader' process reads one frame
    ASSERT_TRUE(ExecuteStep("the 'reader' process reads one frame")) << "Failed: the 'reader' process reads one frame";
    // And the 'reader' process signals space available
    ASSERT_TRUE(ExecuteStep("the 'reader' process signals space available")) << "Failed: the 'reader' process signals space available";
    // Then the 'writer' process should write successfully immediately
    ASSERT_TRUE(ExecuteStep("the 'writer' process should write successfully immediately")) << "Failed: the 'writer' process should write successfully immediately";
}

TEST_F(BasicCommunicationTest, Test_1_4_Zero_Copy_Write_Operations) {
    // Scenario: Test 1.4 - Zero-Copy Write Operations

    // Background steps
    // Given the test environment is initialized
    ASSERT_TRUE(ExecuteStep("the test environment is initialized")) << "Failed: the test environment is initialized";
    // And all processes are ready
    ASSERT_TRUE(ExecuteStep("all processes are ready")) << "Failed: all processes are ready";

    // Scenario steps
    // Given the 'reader' process creates buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'
    ASSERT_TRUE(ExecuteStep("the 'reader' process creates buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'")) << "Failed: the 'reader' process creates buffer 'test-zerocopy' with metadata size '1024' and payload size '102400'";
    // When the 'writer' process connects to buffer 'test-zerocopy'
    ASSERT_TRUE(ExecuteStep("the 'writer' process connects to buffer 'test-zerocopy'")) << "Failed: the 'writer' process connects to buffer 'test-zerocopy'";
    // And the 'writer' process requests zero-copy frame of size '4096'
    ASSERT_TRUE(ExecuteStep("the 'writer' process requests zero-copy frame of size '4096'")) << "Failed: the 'writer' process requests zero-copy frame of size '4096'";
    // And the 'writer' process fills zero-copy buffer with test pattern
    ASSERT_TRUE(ExecuteStep("the 'writer' process fills zero-copy buffer with test pattern")) << "Failed: the 'writer' process fills zero-copy buffer with test pattern";
    // And the 'writer' process commits zero-copy frame
    ASSERT_TRUE(ExecuteStep("the 'writer' process commits zero-copy frame")) << "Failed: the 'writer' process commits zero-copy frame";
    // Then the 'reader' process should read frame with size '4096'
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read frame with size '4096'")) << "Failed: the 'reader' process should read frame with size '4096'";
    // And the 'reader' process should verify frame data matches test pattern
    ASSERT_TRUE(ExecuteStep("the 'reader' process should verify frame data matches test pattern")) << "Failed: the 'reader' process should verify frame data matches test pattern";
}

TEST_F(BasicCommunicationTest, Test_1_5_Mixed_Frame_Sizes) {
    // Scenario: Test 1.5 - Mixed Frame Sizes

    // Background steps
    // Given the test environment is initialized
    ASSERT_TRUE(ExecuteStep("the test environment is initialized")) << "Failed: the test environment is initialized";
    // And all processes are ready
    ASSERT_TRUE(ExecuteStep("all processes are ready")) << "Failed: all processes are ready";

    // Scenario steps
    // Given the 'reader' process creates buffer 'test-mixed' with metadata size '1024' and payload size '102400'
    ASSERT_TRUE(ExecuteStep("the 'reader' process creates buffer 'test-mixed' with metadata size '1024' and payload size '102400'")) << "Failed: the 'reader' process creates buffer 'test-mixed' with metadata size '1024' and payload size '102400'";
    // When the 'writer' process connects to buffer 'test-mixed'
    ASSERT_TRUE(ExecuteStep("the 'writer' process connects to buffer 'test-mixed'")) << "Failed: the 'writer' process connects to buffer 'test-mixed'";
    // And the 'writer' process writes frame with size '100'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with size '100'")) << "Failed: the 'writer' process writes frame with size '100'";
    // And the 'writer' process writes frame with size '1024'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with size '1024'")) << "Failed: the 'writer' process writes frame with size '1024'";
    // And the 'writer' process writes frame with size '10240'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with size '10240'")) << "Failed: the 'writer' process writes frame with size '10240'";
    // And the 'writer' process writes frame with size '1'
    ASSERT_TRUE(ExecuteStep("the 'writer' process writes frame with size '1'")) << "Failed: the 'writer' process writes frame with size '1'";
    // Then the 'reader' process should read 4 frames with sizes '100,1024,10240,1' in order
    ASSERT_TRUE(ExecuteStep("the 'reader' process should read 4 frames with sizes '100,1024,10240,1' in order")) << "Failed: the 'reader' process should read 4 frames with sizes '100,1024,10240,1' in order";
}

