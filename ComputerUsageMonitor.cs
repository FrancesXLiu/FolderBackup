using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

namespace FolderBackup;

public class ComputerUsageMonitor
{
    public static int GetCpuUsage()
    {
        var cpuCounter = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
        cpuCounter.NextValue();
        Thread.Sleep(1000);
        return (int)cpuCounter.NextValue();
    }

    public static int GetPhysicalDisk()
    {
        var physicalDiskCounter = new PerformanceCounter("PhysicalDisk", "% Disk Time", "0 C:");
        physicalDiskCounter.NextValue();
        Thread.Sleep(1000);
        return (int)physicalDiskCounter.NextValue();
    }
}
