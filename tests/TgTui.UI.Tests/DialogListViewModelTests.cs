using FluentAssertions;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Ports;
using TgTui.UI.Fakes;
using TgTui.UI.ViewModels;

namespace TgTui.UI.Tests;

public sealed class DialogListViewModelTests
{
    [Fact]
    public async Task Mute_and_pin_call_service_and_refresh_item()
    {
        var dialogs = new FakeDialogService();
        using var vm = new DialogListViewModel(dialogs);
        await vm.LoadAsync();

        var first = vm.Selected!;
        var mutedBefore = first.IsMuted;
        await vm.ToggleMuteAsync();
        vm.Items.First(d => d.Id.Value == first.Id.Value).IsMuted.Should().Be(!mutedBefore);

        var pinnedBefore = vm.Selected!.IsPinned;
        await vm.TogglePinAsync();
        vm.Items.First(d => d.Id.Value == first.Id.Value).IsPinned.Should().Be(!pinnedBefore);
    }

    [Fact]
    public async Task ClearUnread_zeros_badge_for_chat()
    {
        var dialogs = new FakeDialogService();
        using var vm = new DialogListViewModel(dialogs);
        await vm.LoadAsync();

        var withUnread = vm.Items.FirstOrDefault(d => d.UnreadCount > 0);
        withUnread.Should().NotBeNull();

        vm.ClearUnread(withUnread!.Id);
        vm.Items.First(d => d.Id.Value == withUnread.Id.Value).UnreadCount.Should().Be(0);
    }

    [Fact]
    public async Task ApplyLocalMessage_updates_preview_without_reload()
    {
        var dialogs = new FakeDialogService();
        using var vm = new DialogListViewModel(dialogs);
        await vm.LoadAsync();

        var alice = vm.Items.First(d => d.Id.Value == 1);
        vm.ApplyLocalMessage(alice.Id, new ChatMessage
        {
            Id = new MessageId(500),
            ChatId = alice.Id,
            Text = "local preview text",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
            IsRead = true,
        });

        var updated = vm.Items.First(d => d.Id.Value == 1);
        updated.LastMessagePreview.Should().Be("local preview text");
    }

    [Fact]
    public async Task MessagesChanged_Added_increments_unread_when_chat_not_active()
    {
        var hub = new DialogTestHub();
        var dialogs = new FakeDialogService();
        using var vm = new DialogListViewModel(dialogs, hub);
        await vm.LoadAsync();

        var before = vm.Items.First(d => d.Id.Value == 1).UnreadCount;
        hub.Publish(new MessagesChanged(
            new ChatId(1),
            MessageChangeKind.Added,
            new ChatMessage
            {
                Id = new MessageId(501),
                ChatId = new ChatId(1),
                Text = "ping",
                IsOutgoing = false,
                SentAt = DateTimeOffset.Now,
                IsRead = false,
            }));

        vm.Items.First(d => d.Id.Value == 1).UnreadCount.Should().Be(before + 1);
        vm.Items.First(d => d.Id.Value == 1).LastMessagePreview.Should().Be("ping");
    }

    [Fact]
    public async Task MessagesChanged_Added_does_not_increment_unread_for_active_chat()
    {
        var hub = new DialogTestHub();
        var dialogs = new FakeDialogService();
        using var vm = new DialogListViewModel(dialogs, hub);
        await vm.LoadAsync();
        vm.SetActiveChatId(new ChatId(1));

        hub.Publish(new MessagesChanged(
            new ChatId(1),
            MessageChangeKind.Added,
            new ChatMessage
            {
                Id = new MessageId(502),
                ChatId = new ChatId(1),
                Text = "active chat msg",
                IsOutgoing = false,
                SentAt = DateTimeOffset.Now,
                IsRead = false,
            }));

        vm.Items.First(d => d.Id.Value == 1).UnreadCount.Should().Be(0);
    }

    private sealed class DialogTestHub : IUpdateHub
    {
#pragma warning disable CS0067
        public event Action<DialogsChanged>? DialogsChanged;
        public event Action<ConnectionStateChanged>? ConnectionStateChanged;
        public event Action<AuthStateChanged>? AuthStateChanged;
#pragma warning restore CS0067
        public event Action<MessagesChanged>? MessagesChanged;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Publish(MessagesChanged e) => MessagesChanged?.Invoke(e);
    }
}
