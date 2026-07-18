using FluentAssertions;
using TgTui.Core.Drafts;
using TgTui.Core.Models;
using TgTui.UI.Fakes;
using TgTui.UI.ViewModels;

namespace TgTui.UI.Tests;

public sealed class ComposerViewModelTests
{
    [Fact]
    public async Task StatusHint_shows_reply_snippet_when_resolver_set()
    {
        var messages = new FakeMessageService();
        var drafts = new FileDraftStore(Path.Combine(Path.GetTempPath(), $"tg-tui-drafts-{Guid.NewGuid():N}.json"));
        var composer = new ComposerViewModel(messages, drafts);
        composer.SetReplyPreviewResolver(_ => "original question here");

        await composer.BindChatAsync(new ChatId(1));
        composer.SetReply(new MessageId(80));

        composer.StatusHint.Should().Contain("original question here");
        composer.StatusHint.Should().NotContain("#80");
    }

    [Fact]
    public async Task NeedsQuitConfirmation_true_for_unsent_text_reply_or_edit()
    {
        var messages = new FakeMessageService();
        var drafts = new FileDraftStore(Path.Combine(Path.GetTempPath(), $"tg-tui-drafts-{Guid.NewGuid():N}.json"));
        var composer = new ComposerViewModel(messages, drafts);

        composer.NeedsQuitConfirmation.Should().BeFalse();

        await composer.BindChatAsync(new ChatId(1));
        composer.Text = "draft";
        composer.NeedsQuitConfirmation.Should().BeTrue();

        composer.Text = "";
        composer.SetReply(new MessageId(10));
        composer.NeedsQuitConfirmation.Should().BeTrue();

        composer.CancelReplyOrEdit();
        composer.BeginEdit(new MessageId(20), "edit me");
        composer.NeedsQuitConfirmation.Should().BeTrue();
    }

    [Fact]
    public async Task Submit_send_presents_optimistic_then_confirms_with_replyTo()
    {
        var messages = new FakeMessageService();
        var drafts = new FileDraftStore(Path.Combine(Path.GetTempPath(), $"tg-tui-drafts-{Guid.NewGuid():N}.json"));
        var composer = new ComposerViewModel(messages, drafts);
        await composer.BindChatAsync(new ChatId(1));

        composer.SetReply(new MessageId(80));
        composer.Text = "  hello reply  ";

        ChatMessage? optimistic = null;
        var outcome = await composer.SubmitAsync(msg => optimistic = msg);

        optimistic.Should().NotBeNull();
        optimistic!.Id.Value.Should().BeNegative();
        optimistic.Text.Should().Be("hello reply");
        optimistic.ReplyToId.Should().Be(new MessageId(80));

        outcome.Should().NotBeNull();
        outcome!.IsEdit.Should().BeFalse();
        outcome.ConfirmedMessage.Should().NotBeNull();
        outcome.ConfirmedMessage!.Text.Should().Be("hello reply");
        outcome.ConfirmedMessage.ReplyToId.Should().Be(new MessageId(80));
        outcome.OptimisticMessage!.Id.Should().Be(optimistic.Id);

        composer.Text.Should().BeEmpty();
        composer.ReplyToId.Should().BeNull();
    }

    [Fact]
    public async Task Submit_edit_only_applies_to_edit_target()
    {
        var messages = new FakeMessageService();
        var drafts = new FileDraftStore(Path.Combine(Path.GetTempPath(), $"tg-tui-drafts-{Guid.NewGuid():N}.json"));
        var composer = new ComposerViewModel(messages, drafts);
        await composer.BindChatAsync(new ChatId(1));
        // 78 % 3 == 0 → outgoing in FakeMessageService seed
        composer.BeginEdit(new MessageId(78), "Alice thread msg #78");
        composer.Text = "new edit";

        var outcome = await composer.SubmitAsync();

        outcome.Should().NotBeNull();
        outcome!.IsEdit.Should().BeTrue();
        outcome.EditedMessageId.Should().Be(new MessageId(78));
        outcome.EditedText.Should().Be("new edit");
        composer.EditMessageId.Should().BeNull();
    }

    [Fact]
    public async Task Drafts_persist_across_chat_switches()
    {
        var messages = new FakeMessageService();
        var path = Path.Combine(Path.GetTempPath(), $"tg-tui-drafts-{Guid.NewGuid():N}.json");
        var drafts = new FileDraftStore(path);
        var composer = new ComposerViewModel(messages, drafts);

        await composer.BindChatAsync(new ChatId(1));
        composer.Text = "draft for alice";
        await composer.BindChatAsync(new ChatId(2));
        composer.Text.Should().BeEmpty();
        composer.Text = "draft for work";

        await composer.BindChatAsync(new ChatId(1));
        composer.Text.Should().Be("draft for alice");

        await composer.BindChatAsync(new ChatId(2));
        composer.Text.Should().Be("draft for work");
    }
}
