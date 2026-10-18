namespace Shop.Shared;

// Compiled into both Shop.Web and Shop.Stock (like eShop's src/Shared): one file, two projects.
public static class Clock
{
    public static DateTime Now() => DateTime.UtcNow;
}
