using System;
using Microsoft.Win32;
using wumgr;

class PolicyTests
{
    static int passed;
    static void Check(bool ok, string name) { if (!ok) throw new Exception("失败：" + name); passed++; Console.WriteLine("通过：" + name); }

    sealed class FakeStore : IPolicyStore
    {
        public bool IsWindows = true;
        public bool IsAdmin = true;
        public PolicyValue Current;
        public bool Has;
        public PolicyValue Backup;
        public int CreateCount;
        public bool WriteFails;
        public bool IsWindowsValue { get { return IsWindows; } }
        bool IPolicyStore.IsWindows { get { return IsWindows; } }
        bool IPolicyStore.IsAdministrator { get { return IsAdmin; } }
        PolicyValue IPolicyStore.ReadPolicyValue() { return Current; }
        bool IPolicyStore.BackupExists { get { return Has; } }
        PolicyValue IPolicyStore.ReadBackup() { return Backup; }
        void IPolicyStore.CreateBackup(PolicyValue p) { Backup = p; Has = true; CreateCount++; }
        void IPolicyStore.WritePolicyValue(int value) { if (WriteFails) throw new InvalidOperationException("模拟写入失败"); Current = new PolicyValue { Exists = true, Kind = RegistryValueKind.DWord, Value = value }; }
        void IPolicyStore.DeletePolicyValue() { Current = new PolicyValue { Exists = false }; }
        void IPolicyStore.DeleteBackup() { Has = false; }
    }

    static void Main()
    {
        var fake = new FakeStore { Current = new PolicyValue { Exists = true, Kind = RegistryValueKind.DWord, Value = 7 } };
        var policy = new DriverUpdatePolicy(fake);
        policy.Enable(); policy.Enable();
        Check(fake.CreateCount == 1 && fake.Backup.Value == 7 && policy.Read().Enabled, "启用幂等且不覆盖原始备份");
        policy.Restore();
        Check(!fake.Has && fake.Current.Exists && fake.Current.Value == 7, "恢复原值并清理备份");

        fake = new FakeStore { Current = new PolicyValue { Exists = true, Kind = RegistryValueKind.DWord, Value = 4 } };
        policy = new DriverUpdatePolicy(fake); policy.Enable();
        fake.Current.Value = 2;
        bool rejected = false; try { policy.Restore(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && fake.Has, "外部改变时拒绝覆盖且保留备份");

        fake = new FakeStore { IsAdmin = false };
        bool denied = false; try { new DriverUpdatePolicy(fake).Enable(); } catch (UnauthorizedAccessException) { denied = true; }
        Check(denied && !fake.Has, "非管理员不能创建备份或写策略");

        fake = new FakeStore { IsWindows = false };
        bool platform = false; try { new DriverUpdatePolicy(fake).Read(); } catch (PlatformNotSupportedException) { platform = true; }
        Check(platform, "非 Windows 明确异常");

        fake = new FakeStore { Current = new PolicyValue { Exists = true, Kind = RegistryValueKind.String, Value = 1 } };
        bool typeRejected = false; try { new DriverUpdatePolicy(fake).Enable(); } catch (InvalidOperationException) { typeRejected = true; }
        Check(typeRejected && !fake.Has, "非 DWORD 原值拒绝处理");
        foreach (int original in new[] { -1, 0, 1 })
        {
            fake = new FakeStore { Current = new PolicyValue { Exists = original >= 0, Kind = RegistryValueKind.DWord, Value = Math.Max(0, original) } };
            policy = new DriverUpdatePolicy(fake); policy.Enable(); policy.Enable(); policy.Restore();
            Check(fake.Current.Exists == (original >= 0) && (!fake.Current.Exists || fake.Current.Value == original) && !fake.Has, "恢复原策略值 " + original + "（-1表示不存在）");
        }
        fake = new FakeStore { WriteFails = true }; policy = new DriverUpdatePolicy(fake);
        bool failed = false; try { policy.Enable(); } catch (InvalidOperationException) { failed = true; }
        Check(failed && fake.Has && !fake.Current.Exists, "策略写入失败时保留原始备份且不伪报成功");
        fake = new FakeStore { Has = true, Backup = new PolicyValue { Exists = true, Kind = RegistryValueKind.String }, Current = new PolicyValue { Exists = true, Kind = RegistryValueKind.DWord, Value = 1 } }; policy = new DriverUpdatePolicy(fake);
        bool corrupted = false; try { policy.Enable(); } catch (InvalidOperationException) { corrupted = true; }
        Check(corrupted && fake.Has && fake.CreateCount == 0, "损坏备份拒绝被重复开启覆盖");
        Console.WriteLine("策略测试完成：" + passed + " 项全部通过。");
    }
}
