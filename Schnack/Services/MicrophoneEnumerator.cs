using NAudio.Wave;

namespace Schnack.Services;

public static class MicrophoneEnumerator
{
    /// <summary>
    /// Liefert alle WaveIn-Aufnahmegeräte (Index 0 … n-1). Standardgerät wählt der Aufrufer per null in den Settings.
    /// </summary>
    public static IReadOnlyList<(int DeviceIndex, string Name)> ListCaptureDevices()
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < WaveIn.DeviceCount; i++)
        {
            var cap = WaveIn.GetCapabilities(i);
            list.Add((i, string.IsNullOrWhiteSpace(cap.ProductName)
                ? Localization.Strings.Format(nameof(Localization.Strings.Settings_MicFallback), i)
                : cap.ProductName));
        }

        return list;
    }
}
