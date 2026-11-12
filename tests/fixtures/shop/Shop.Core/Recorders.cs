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

namespace Shop.Core
{
    // Step 6: a base class's virtual that only sets a property, which one subclass overrides and another doesn't.
    public class Stamp { public string Name { get; set; } = ""; }

    public class Stamper
    {
        public virtual void Mark(Stamp stamp) { stamp.Name = "plain"; }
    }

    public class LoudStamper : Stamper
    {
        public override void Mark(Stamp stamp) { stamp.Name = "LOUD"; }
    }

    public class QuietStamper : Stamper
    {
    }
}
