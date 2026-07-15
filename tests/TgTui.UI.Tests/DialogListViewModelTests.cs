using FluentAssertions;
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
}
