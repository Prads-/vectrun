namespace vectrun.tests.Pipeline;

using vectrun.Pipeline;

public class AgentMessageQueuesTests
{
    [Fact]
    public void Dequeue_EmptyQueue_ReturnsNull()
    {
        var queues = new AgentMessageQueues();
        Assert.Null(queues.Dequeue("agent1"));
    }

    [Fact]
    public void Dequeue_UnknownAgent_ReturnsNull()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "hello");
        Assert.Null(queues.Dequeue("agent2"));
    }

    [Fact]
    public void Enqueue_ThenDequeue_ReturnsSameMessage()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "hello");
        Assert.Equal("hello", queues.Dequeue("agent1"));
    }

    [Fact]
    public void Dequeue_AfterExhausted_ReturnsNull()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "msg");
        queues.Dequeue("agent1");
        Assert.Null(queues.Dequeue("agent1"));
    }

    [Fact]
    public void Enqueue_MultipleMessages_DequeuesInOrder()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "first");
        queues.Enqueue("agent1", "second");
        queues.Enqueue("agent1", "third");

        Assert.Equal("first", queues.Dequeue("agent1"));
        Assert.Equal("second", queues.Dequeue("agent1"));
        Assert.Equal("third", queues.Dequeue("agent1"));
        Assert.Null(queues.Dequeue("agent1"));
    }

    [Fact]
    public void Enqueue_MultipleAgents_QueuesAreIndependent()
    {
        var queues = new AgentMessageQueues();
        queues.Enqueue("agent1", "for-agent1");
        queues.Enqueue("agent2", "for-agent2");

        Assert.Equal("for-agent1", queues.Dequeue("agent1"));
        Assert.Equal("for-agent2", queues.Dequeue("agent2"));
    }
}
