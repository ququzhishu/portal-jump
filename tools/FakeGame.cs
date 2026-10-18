using System;
using System.Threading;

/// <summary>假"游戏"进程，只用来验证 折返跃迁 的自动切换，本身什么都不做。</summary>
static class FakeGame
{
    static void Main(string[] args)
    {
        int seconds = 60;
        if (args.Length > 0) int.TryParse(args[0], out seconds);
        Thread.Sleep(seconds * 1000);
    }
}
