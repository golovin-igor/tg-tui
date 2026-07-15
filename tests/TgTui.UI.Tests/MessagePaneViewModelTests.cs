using FluentAssertions;
using TgTui.Core.Models;
using TgTui.UI.Fakes;
using TgTui.UI.ViewModels;

namespace TgTui.UI.Tests;

public sealed class MessagePaneViewModelTests
{
    [Fact]
    public async Task OpenChat_loads_latest_history_page()
    {
        var messages = new FakeMessageService();
        var media = new FakeMediaService();
        using var vm = new MessagePaneViewModel(messages, media);

        await vm.OpenChatAsync(Alice());

        vm.Messages.Should().HaveCount(50);
        vm.Messages[0].Id.Value.Should().Be(31);
        vm.Messages[^1].Id.Value.Should().Be(80);
        vm.HasMoreHistory.Should().BeTrue();
        vm.Selected!.Id.Value.Should().Be(80);
    }

    [Fact]
    public async Task LoadOlder_prepends_previous_page_and_preserves_selection()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        var selectedBefore = vm.Selected!.Id.Value;

        await vm.LoadOlderAsync();

        vm.Messages.Should().HaveCount(80);
        vm.Messages[0].Id.Value.Should().Be(1);
        vm.Selected!.Id.Value.Should().Be(selectedBefore);
        vm.HasMoreHistory.Should().BeFalse();
    }

    [Fact]
    public async Task PresentOptimistic_then_Confirm_replaces_temp_row()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        var before = vm.Messages.Count;

        var optimistic = new ChatMessage
        {
            Id = new MessageId(-1),
            ChatId = new ChatId(1),
            Text = "pending",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
            ReplyToId = new MessageId(80),
        };
        vm.PresentOptimistic(optimistic);
        vm.Messages.Should().HaveCount(before + 1);
        vm.Messages[^1].Id.Value.Should().Be(-1);

        var confirmed = new ChatMessage
        {
            Id = new MessageId(9001),
            ChatId = new ChatId(1),
            Text = "pending",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
            ReplyToId = new MessageId(80),
        };
        vm.ConfirmOptimistic(optimistic.Id, confirmed);

        vm.Messages.Should().HaveCount(before + 1);
        vm.Messages.Should().NotContain(m => m.Id.Value == -1);
        vm.Messages[^1].Id.Value.Should().Be(9001);
        vm.Messages[^1].ReplyToId.Should().Be(new MessageId(80));
    }

    [Fact]
    public async Task CancelOptimistic_removes_temp_row()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        var before = vm.Messages.Count;

        vm.PresentOptimistic(new ChatMessage
        {
            Id = new MessageId(-2),
            ChatId = new ChatId(1),
            Text = "fail me",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
        });
        vm.CancelOptimistic(new MessageId(-2));

        vm.Messages.Should().HaveCount(before);
        vm.Messages.Should().NotContain(m => m.Id.Value == -2);
    }

    [Fact]
    public async Task DeleteSelected_removes_from_list_on_success()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        vm.SelectedIndex = vm.Messages.Count - 1;
        var id = vm.Selected!.Id;

        await vm.DeleteSelectedAsync();

        vm.Messages.Should().NotContain(m => m.Id.Value == id.Value);
    }

    [Fact]
    public async Task EditSelected_only_outgoing()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());

        // Seed: i % 3 == 0 → outgoing. Page is 31..80 → 78 outgoing, 79/80 incoming.
        var outgoingIdx = vm.Messages.ToList().FindIndex(m => m.Id.Value == 78);
        var incomingIdx = vm.Messages.ToList().FindIndex(m => m.Id.Value == 79);
        outgoingIdx.Should().BeGreaterThanOrEqualTo(0);
        incomingIdx.Should().BeGreaterThanOrEqualTo(0);

        vm.SelectedIndex = incomingIdx;
        vm.Selected!.IsOutgoing.Should().BeFalse();
        await vm.EditSelectedAsync("nope");
        vm.Selected!.Text.Should().Be("Alice thread msg #79");

        vm.SelectedIndex = outgoingIdx;
        vm.Selected!.IsOutgoing.Should().BeTrue();
        await vm.EditSelectedAsync("edited body");
        vm.Selected!.Text.Should().Be("edited body");
        vm.Selected.IsEdited.Should().BeTrue();
    }

    private static DialogItem Alice() =>
        new()
        {
            Id = new ChatId(1),
            Title = "Alice",
            LastMessagePreview = "x",
            LastMessageAt = DateTimeOffset.Now,
            UnreadCount = 0,
            IsPinned = false,
            IsMuted = false,
            AvatarLetter = 'A',
        };
}
