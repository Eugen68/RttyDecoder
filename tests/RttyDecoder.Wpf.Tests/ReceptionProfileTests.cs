using RttyDecoder.Core;
using RttyDecoder.Wpf;

namespace RttyDecoder.Wpf.Tests;

public class ReceptionProfileTests
{
    [Fact]
    public async Task ProfilulSiControaleleUrmatorescDecodorulSiReglajeleManuale()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                using var receiver = new RttyReceiver((input, dims) => null);
                using var vm = new RttyDecoderViewModel(receiver);
                vm.ImportSettings(new RttySettings { MarkFrequencyHz = 1450, Reverse = true, UsTty = true, Tracking = RttyTracking.Afc });
                vm.SelectedReceptionProfile = vm.ReceptionProfiles.Single(p => p.Id == RttyReceptionProfile.WeakSignal);
                Assert.Equal(RttyDecoderType.DeepRtty, vm.Decoder);
                Assert.False(vm.IsClassic);
                Assert.False(vm.IsChebyshevDecoder);
                Assert.False(vm.CanAdjustSquelch);
                Assert.True(vm.IsChebyshev); // filtre clasice pentru SNR/AFC
                Assert.Contains("audio-ul original", vm.DecoderHelp);
                Assert.Equal(1450, vm.MarkFrequencyHz);
                Assert.True(vm.Reverse);
                Assert.True(vm.UsTty);
                Assert.Equal(RttyTracking.Afc, vm.Tracking);
                vm.SelectedReceptionProfile = vm.ReceptionProfiles.Single(p => p.Id == RttyReceptionProfile.GoodSignal);
                Assert.Equal(RttyDecoderType.MatchedFilter, vm.Decoder);
                Assert.True(vm.IsClassic);
                Assert.False(vm.IsChebyshev);
                Assert.True(vm.CanAdjustSquelch);
                vm.SensitivityPercent = 12;
                Assert.Null(vm.SelectedReceptionProfile!.Id);
                vm.SelectedReceptionProfile = vm.ReceptionProfiles.Single(p => p.Id == RttyReceptionProfile.QrnFlutter);
                var saved = vm.ExportSettings();
                vm.SelectedReceptionProfile = vm.ReceptionProfiles[0];
                vm.ImportSettings(saved);
                Assert.Equal(RttyReceptionProfile.QrnFlutter, vm.SelectedReceptionProfile!.Id);
                Assert.True(vm.IsChebyshevDecoder);
                Assert.False(vm.NoiseBlanker);
                Assert.False(vm.Atc);
                done.SetResult();
            }
            catch (Exception error) { done.SetException(error); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await done.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }
}
