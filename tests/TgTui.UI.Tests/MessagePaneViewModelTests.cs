using FluentAssertions;
using TgTui.Core.Events;
using TgTui.Core.Models;
using TgTui.Core.Ports;
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
    public async Task OpenChat_raises_ChatMarkedRead()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        ChatId? marked = null;
        vm.ChatMarkedRead += id => marked = id;

        await vm.OpenChatAsync(Alice());

        marked.Should().Be(new ChatId(1));
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
    public async Task ReloadAsync_preserves_selection_when_not_at_latest()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        vm.SelectedIndex = 5;
        var selectedId = vm.Selected!.Id;

        await vm.ReloadAsync(preserveScrollPosition: true);

        vm.Selected!.Id.Should().Be(selectedId);
        vm.SelectedIndex.Should().Be(5);
    }

    [Fact]
    public async Task ReloadAsync_jumps_to_latest_when_already_at_bottom()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());
        vm.SelectedIndex.Should().Be(vm.Messages.Count - 1);

        await vm.ReloadAsync(preserveScrollPosition: true);

        vm.SelectedIndex.Should().Be(vm.Messages.Count - 1);
    }

    [Fact]
    public async Task IncrementalAdded_preserves_scroll_when_not_at_latest()
    {
        var hub = new TestUpdateHub();
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService(), hub);
        await vm.OpenChatAsync(Alice());
        vm.SelectedIndex = 5;
        var selectedId = vm.Selected!.Id;

        hub.Publish(new MessagesChanged(
            new ChatId(1),
            MessageChangeKind.Added,
            new ChatMessage
            {
                Id = new MessageId(9999),
                ChatId = new ChatId(1),
                Text = "incremental",
                IsOutgoing = false,
                SentAt = DateTimeOffset.Now,
                IsRead = true,
            }));

        await WaitUntilAsync(() => vm.Messages.Any(m => m.Id.Value == 9999));
        vm.Selected!.Id.Should().Be(selectedId);
        vm.Messages[^1].Text.Should().Be("incremental");
    }

    [Fact]
    public async Task SearchQuery_finds_matches_and_moves_selection()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());

        vm.SearchQuery = "Alice thread";
        vm.SearchMatchCount.Should().BeGreaterThan(0);
        vm.MoveSearchMatch(1).Should().BeTrue();
        vm.Selected!.Text.Should().Contain("Alice thread");
    }

    [Fact]
    public async Task FormatRow_shows_reply_snippet_instead_of_raw_id()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());

        var replyTarget = vm.Messages[^1];
        vm.PresentOptimistic(new ChatMessage
        {
            Id = new MessageId(-3),
            ChatId = new ChatId(1),
            Text = "sure thing",
            IsOutgoing = true,
            SentAt = DateTimeOffset.Now,
            ReplyToId = replyTarget.Id,
        });

        var row = vm.FormatRow(vm.Messages[^1], 120);
        row.Should().Contain("↩");
        row.Should().NotContain($"↩ {replyTarget.Id.Value}");
        row.Should().Contain(replyTarget.Text.Replace('\n', ' '));
    }

    [Fact]
    public async Task GetReplySnippet_returns_text_or_media_label()
    {
        var messages = new FakeMessageService();
        using var vm = new MessagePaneViewModel(messages, new FakeMediaService());
        await vm.OpenChatAsync(Alice());

        var target = vm.Messages[^1];
        vm.GetReplySnippet(target.Id).Should().Be(target.Text.Replace('\n', ' ').Trim());
        vm.GetReplySnippet(new MessageId(99999)).Should().BeNull();
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

    [Fact]
    public async Task EnsureMediaPreview_shows_loading_then_preview()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var media = new ControllableMediaService
        {
            EnsureGate = gate.Task,
            LocalPath = "/tmp/preview.png",
            Preview = "🖼 half-block-line",
        };
        var messages = new SingleChatMessageService(MediaPhotoMessage());
        using var vm = new MessagePaneViewModel(messages, media);

        var open = vm.OpenChatAsync(Design());
        await WaitUntilAsync(() =>
            vm.GetMediaPreview(new MessageId(501)) == MessagePaneViewModel.MediaLoadingPlaceholder);

        vm.GetMediaPreview(new MessageId(501)).Should().Be(MessagePaneViewModel.MediaLoadingPlaceholder);
        vm.FormatRow(vm.Messages[0], 120).Should().Contain("loading");

        gate.SetResult();
        await open;
        await WaitUntilAsync(() =>
            vm.GetMediaPreview(new MessageId(501)) == "🖼 half-block-line");

        vm.GetMediaPreview(new MessageId(501)).Should().Be("🖼 half-block-line");
        media.EnsureCalls.Should().Be(1);
        vm.FormatRow(vm.Messages[0], 120).Should().Contain("half-block-line");
    }

    [Fact]
    public async Task EnsureMediaPreview_failure_shows_unavailable_without_throwing()
    {
        var media = new ControllableMediaService { FailEnsure = true };
        var messages = new SingleChatMessageService(MediaPhotoMessage());
        using var vm = new MessagePaneViewModel(messages, media);

        await vm.OpenChatAsync(Design());
        await WaitUntilAsync(() =>
            vm.GetMediaPreview(new MessageId(501)) == MessagePaneViewModel.MediaUnavailablePlaceholder);

        vm.GetMediaPreview(new MessageId(501)).Should().Be(MessagePaneViewModel.MediaUnavailablePlaceholder);
        vm.FormatRow(vm.Messages[0], 120).Should().Contain("image unavailable");
        vm.MoveSelection(0); // still navigable
        vm.SelectedIndex.Should().Be(0);
    }

    [Fact]
    public async Task OpenSelectedMediaExternally_ensures_local_then_opens()
    {
        var media = new ControllableMediaService { LocalPath = "/tmp/open-me.png" };
        var messages = new SingleChatMessageService(MediaPhotoMessage());
        using var vm = new MessagePaneViewModel(messages, media);
        await vm.OpenChatAsync(Design());
        await WaitUntilAsync(() => vm.GetMediaPreview(new MessageId(501)) is not null);

        await vm.OpenSelectedMediaExternallyAsync();

        media.OpenCalls.Should().Be(1);
        media.OpenedPaths.Should().ContainSingle().Which.Should().Be("/tmp/open-me.png");
    }

    [Fact]
    public async Task FakeMediaService_shows_open_placeholder_for_photo()
    {
        var media = new FakeMediaService();
        var messages = new SingleChatMessageService(MediaPhotoMessage());
        using var vm = new MessagePaneViewModel(messages, media);

        await vm.OpenChatAsync(Design());
        await WaitUntilAsync(() =>
            vm.GetMediaPreview(new MessageId(501)) == FakeMediaService.PlaceholderPreview);

        vm.GetMediaPreview(new MessageId(501)).Should().Be(FakeMediaService.PlaceholderPreview);
        await vm.OpenSelectedMediaExternallyAsync();
        media.OpenedPaths.Should().ContainSingle().Which.Should().Be(FakeMediaService.PlaceholderPath);
    }

    [Fact]
    public async Task Document_media_shows_label_without_download()
    {
        var media = new ControllableMediaService();
        var doc = new ChatMessage
        {
            Id = new MessageId(900),
            ChatId = new ChatId(5),
            Text = "",
            IsOutgoing = false,
            SentAt = DateTimeOffset.Now,
            Media = new MediaAttachment
            {
                Kind = "document",
                FileName = "spec.pdf",
                MimeType = "application/pdf",
                SourceChatId = new ChatId(5),
                SourceMessageId = new MessageId(900),
            },
            IsRead = true,
        };
        var messages = new SingleChatMessageService(doc);
        using var vm = new MessagePaneViewModel(messages, media);

        await vm.OpenChatAsync(Design());
        await WaitUntilAsync(() => vm.GetMediaPreview(new MessageId(900)) is not null);

        vm.GetMediaPreview(new MessageId(900)).Should().Contain("spec.pdf");
        media.EnsureCalls.Should().Be(0);
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

    private static DialogItem Design() =>
        new()
        {
            Id = new ChatId(5),
            Title = "Design Team",
            LastMessagePreview = "mockups",
            LastMessageAt = DateTimeOffset.Now,
            UnreadCount = 1,
            IsPinned = false,
            IsMuted = false,
            AvatarLetter = 'D',
        };

    private static ChatMessage MediaPhotoMessage() =>
        new()
        {
            Id = new MessageId(501),
            ChatId = new ChatId(5),
            Text = "New mockups attached",
            IsOutgoing = false,
            SentAt = DateTimeOffset.Now,
            Media = new MediaAttachment
            {
                Kind = "photo",
                FileName = "mockup.png",
                MimeType = "image/png",
                SourceChatId = new ChatId(5),
                SourceMessageId = new MessageId(501),
            },
            IsRead = true,
        };

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = Environment.TickCount64;
        while (Environment.TickCount64 - start < timeoutMs)
        {
            if (condition())
                return;
            await Task.Delay(10);
        }

        condition().Should().BeTrue("condition should become true within timeout");
    }

    private sealed class TestUpdateHub : IUpdateHub
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

    private sealed class SingleChatMessageService : IMessageService
    {
        private readonly List<ChatMessage> _items;

        public SingleChatMessageService(params ChatMessage[] items) =>
            _items = items.ToList();

        public Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
            ChatId chatId,
            MessageId? beforeId,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<ChatMessage> q = _items.Where(m => m.ChatId.Value == chatId.Value);
            if (beforeId is { } b)
                q = q.Where(m => m.Id.Value < b.Value);
            var page = q.OrderByDescending(m => m.Id.Value)
                .Take(Math.Max(1, limit))
                .OrderBy(m => m.Id.Value)
                .ToList();
            return Task.FromResult<IReadOnlyList<ChatMessage>>(page);
        }

        public Task<ChatMessage> SendTextAsync(
            ChatId chatId, string text, MessageId? replyToId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task EditTextAsync(
            ChatId chatId, MessageId messageId, string text, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(ChatId chatId, MessageId messageId, CancellationToken cancellationToken = default)
        {
            _items.RemoveAll(m => m.Id.Value == messageId.Value);
            return Task.CompletedTask;
        }

        public Task MarkReadAsync(ChatId chatId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class ControllableMediaService : IMediaService
    {
        public Task? EnsureGate { get; init; }
        public string? LocalPath { get; init; }
        public string Preview { get; init; } = "🖼 preview";
        public bool FailEnsure { get; init; }
        public bool ThrowOnEnsure { get; init; }
        public int EnsureCalls { get; private set; }
        public int OpenCalls { get; private set; }
        public List<string> OpenedPaths { get; } = [];

        public async Task<string?> EnsureLocalAsync(
            MediaAttachment media,
            CancellationToken cancellationToken = default)
        {
            EnsureCalls++;
            if (EnsureGate is not null)
                await EnsureGate.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnEnsure)
                throw new IOException("download failed");
            if (FailEnsure)
                return null;
            return LocalPath;
        }

        public Task OpenExternallyAsync(string localPath, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            OpenedPaths.Add(localPath);
            return Task.CompletedTask;
        }

        public string RenderPreview(string localPath, int maxCellWidth) => Preview;
    }
}
