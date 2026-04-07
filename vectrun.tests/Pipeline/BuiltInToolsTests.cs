namespace vectrun.tests.Pipeline;

using vectrun.Pipeline;

public class BuiltInToolsTests
{
    // MyMessageQueueTool

    [Fact]
    public async Task MyMessageQueue_EmptyQueue_ReturnsEmptyString()
    {
        var tool = new MyMessageQueueTool(new AgentMessageQueues());
        var result = await tool.ExecuteAsync("""{"agentId":"agent1"}""", default);
        Assert.Equal("", result);
    }

    [Fact]
    public async Task MyMessageQueue_WithMessage_ReturnsAndRemovesIt()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "hello");

        var tool = new MyMessageQueueTool(queues);
        var result = await tool.ExecuteAsync("""{"agentId":"agent1"}""", default);

        Assert.Equal("hello", result);
        Assert.Null(queues.Dequeue("agent1")); // consumed
    }

    [Fact]
    public void MyMessageQueue_Name_IsReserved()
    {
        var tool = new MyMessageQueueTool(new AgentMessageQueues());
        Assert.Equal("my_message_queue", tool.Name);
    }

    // QueueMessageTool

    [Fact]
    public async Task QueueMessage_EnqueuesMessage()
    {
        var queues = new AgentMessageQueues();
        var tool = new QueueMessageTool(queues);

        await tool.ExecuteAsync("""{"agentId":"agent1","message":"hi there"}""", default);

        Assert.Equal("hi there", queues.Dequeue("agent1"));
    }

    [Fact]
    public async Task QueueMessage_ReturnsOk()
    {
        var tool = new QueueMessageTool(new AgentMessageQueues());
        var result = await tool.ExecuteAsync("""{"agentId":"agent1","message":"msg"}""", default);
        Assert.Equal("ok", result);
    }

    [Fact]
    public void QueueMessage_Name_IsReserved()
    {
        var tool = new QueueMessageTool(new AgentMessageQueues());
        Assert.Equal("queue_message", tool.Name);
    }
}
