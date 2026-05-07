namespace Schnack.Services;

public interface IRecordingService : IDisposable
{
    bool IsRecording { get; }
    void StartRecording(string filePath);
    string StopRecording();
}
