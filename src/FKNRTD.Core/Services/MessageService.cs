using FKNRTD.Domain;

namespace FKNRTD.Services;

public sealed class MessageService
{
    private readonly StateStore _store;

    public MessageService(StateStore store)
    {
        _store = store;
    }

    public async Task<AgentMessage> SendAsync(
        string fromAgentId,
        string toAgentId,
        string text,
        string? taskId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("A message cannot be empty.", nameof(text));
        }

        var message = new AgentMessage
        {
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            TaskId = taskId,
            Text = text.Trim(),
            Delivery = MessageDelivery.Delivered
        };
        await _store.AppendMessageAsync(message, cancellationToken).ConfigureAwait(false);
        await _store.AppendEventAsync(new FknrtdEvent
        {
            Type = "message.sent",
            AgentId = fromAgentId,
            TaskId = taskId,
            Message = $"{fromAgentId} sent a message to {toAgentId}: {Preview(text, 80)}"
        }, cancellationToken).ConfigureAwait(false);
        return message;
    }

    public async Task AcknowledgeAsync(string messageId, CancellationToken cancellationToken = default)
    {
        var messages = await GetCurrentAsync(1000, cancellationToken).ConfigureAwait(false);
        var original = messages.FirstOrDefault(item => item.Id.Equals(messageId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Message '{messageId}' was not found.");
        var acknowledged = original with
        {
            Delivery = MessageDelivery.Acknowledged,
            AcknowledgedAt = DateTimeOffset.UtcNow
        };
        await _store.AppendMessageAsync(acknowledged, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentMessage>> GetCurrentAsync(
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var history = await _store.LoadMessagesAsync(Math.Max(limit * 4, limit), cancellationToken)
            .ConfigureAwait(false);
        return history
            .GroupBy(message => message.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.AcknowledgedAt ?? item.CreatedAt).First())
            .OrderByDescending(item => item.CreatedAt)
            .Take(limit)
            .ToArray();
    }

    private static string Preview(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..Math.Max(1, maxLength - 1)] + "…";
}
