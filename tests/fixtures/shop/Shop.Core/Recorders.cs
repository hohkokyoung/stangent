namespace Shop.Core
{
    // Step 6: an interface member implemented by a base class that doesn't list the interface itself.
    public interface IRecorder
    {
        void Record(string line);
    }

    public abstract class RecorderBase
    {
        public void Record(string line) => System.Console.WriteLine(line);
    }

    public sealed class ConsoleRecorder : RecorderBase, IRecorder
    {
    }
}
