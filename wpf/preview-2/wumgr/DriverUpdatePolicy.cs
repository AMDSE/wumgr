using System;
using Microsoft.Win32;

namespace wumgr
{
    public sealed class PolicyState
    {
        public bool Enabled { get; private set; }
        public string Description { get; private set; }

        public PolicyState(bool enabled, string description)
        {
            Enabled = enabled;
            Description = description ?? String.Empty;
        }
    }

    // Internal by design: tests can inject it by compiling this file with the test assembly,
    // while production callers use the registry-backed parameterless constructor.
    internal struct PolicyValue
    {
        public bool Exists;
        public RegistryValueKind Kind;
        public int Value;
    }

    internal interface IPolicyStore
    {
        bool IsWindows { get; }
        bool IsAdministrator { get; }
        PolicyValue ReadPolicyValue();
        bool BackupExists { get; }
        PolicyValue ReadBackup();
        void CreateBackup(PolicyValue original);
        void WritePolicyValue(int value);
        void DeletePolicyValue();
        void DeleteBackup();
    }

    public sealed class DriverUpdatePolicy
    {
        private const string PolicyPath = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string PolicyName = "ExcludeWUDriversInQualityUpdate";
        private const string BackupPath = @"Software\WuMgrDrivers\PolicyBackup";
        private readonly IPolicyStore store;

        public DriverUpdatePolicy() : this(new RegistryPolicyStore()) { }

        internal DriverUpdatePolicy(IPolicyStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
        }

        public bool HasBackup
        {
            get { EnsurePlatform(); return store.BackupExists; }
        }

        public PolicyState Read()
        {
            EnsurePlatform();
            PolicyValue value = store.ReadPolicyValue();
            if (value.Exists && value.Kind != RegistryValueKind.DWord)
                throw new InvalidOperationException("Windows 驱动更新策略的注册表值类型不受支持。");
            return State(value.Exists && value.Value == 1);
        }

        public void Enable()
        {
            EnsureAdministrator();
            PolicyValue current = store.ReadPolicyValue();
            if (current.Exists && current.Kind != RegistryValueKind.DWord)
                throw new InvalidOperationException("原驱动更新策略不是 DWORD 类型，拒绝覆盖。");

            if (store.BackupExists)
            {
                // A malformed/tampered backup must never be silently replaced.
                ValidateBackup(store.ReadBackup());
            }
            else
            {
                // Create the backup completely before changing Windows Update policy.
                store.CreateBackup(current);
            }
            try
            {
                store.WritePolicyValue(1);
            }
            catch
            {
                // Deliberately retain the backup so a later restore can recover the original value.
                throw;
            }
        }

        public void Restore()
        {
            EnsureAdministrator();
            if (!store.BackupExists)
                throw new InvalidOperationException("没有本工具保存的驱动更新策略备份，无法恢复。");

            PolicyValue backup = store.ReadBackup();
            ValidateBackup(backup);
            PolicyValue current = store.ReadPolicyValue();
            if (!current.Exists || current.Kind != RegistryValueKind.DWord || current.Value != 1)
                throw new InvalidOperationException("策略已被外部修改，或当前并非本工具开启的保护，拒绝覆盖恢复。");

            if (backup.Exists) store.WritePolicyValue(backup.Value);
            else store.DeletePolicyValue();
            store.DeleteBackup();
        }

        private void EnsurePlatform()
        {
            if (!store.IsWindows)
                throw new PlatformNotSupportedException("驱动更新保护需要 Windows 系统。");
        }

        private void EnsureAdministrator()
        {
            EnsurePlatform();
            if (!store.IsAdministrator)
                throw new UnauthorizedAccessException("修改全机驱动更新策略需要管理员权限，请右键以管理员身份运行。");
        }

        private static void ValidateBackup(PolicyValue backup)
        {
            if (backup.Exists && backup.Kind != RegistryValueKind.DWord)
                throw new InvalidOperationException("驱动更新策略备份无效，拒绝覆盖。");
        }

        private static PolicyState State(bool enabled)
        {
            string status = enabled ? "已开启" : "未开启";
            return new PolicyState(enabled, status + "：仅排除 Windows Update 的常规质量更新驱动。该策略是全局计算机策略，可能被域策略或 MDM 覆盖；随系统安全／关键更新提供的驱动、功能更新动态驱动不在阻断范围内；设备初装和手动安装也可能更改驱动。不会自动重启，请自行核验生效情况。");
        }

        private sealed class RegistryPolicyStore : IPolicyStore
        {
            private const string Complete = "Complete";
            private const string Exists = "OriginalExists";
            private const string Kind = "OriginalKind";
            private const string Value = "OriginalValue";

            public bool IsWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }
            public bool IsAdministrator
            {
                get
                {
                    if (!IsWindows) return false;
                    using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
                    {
                        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    }
                }
            }

            private RegistryKey Base()
            {
                return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            }

            public PolicyValue ReadPolicyValue()
            {
                using (var root = Base())
                using (var key = root.OpenSubKey(PolicyPath, false))
                {
                    if (key == null || key.GetValue(PolicyName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) == null)
                        return new PolicyValue { Exists = false };
                    var kind = key.GetValueKind(PolicyName);
                    return new PolicyValue { Exists = true, Kind = kind, Value = kind == RegistryValueKind.DWord ? Convert.ToInt32(key.GetValue(PolicyName)) : 0 };
                }
            }

            public bool BackupExists
            {
                get
                {
                    using (var root = Base()) using (var key = root.OpenSubKey(BackupPath, false))
                        // Presence (including an incomplete key left by a failed write) is
                        // intentional: Enable must never overwrite a possibly recoverable backup.
                        return key != null;
                }
            }

            public PolicyValue ReadBackup()
            {
                using (var root = Base()) using (var key = root.OpenSubKey(BackupPath, false))
                {
                    if (key == null || Convert.ToInt32(key.GetValue(Complete, 0)) != 1)
                        throw new InvalidOperationException("驱动更新策略备份不完整，拒绝覆盖；请联系系统管理员检查备份。");
                    return new PolicyValue { Exists = Convert.ToInt32(key.GetValue(Exists, 0)) != 0,
                        Kind = (RegistryValueKind)Convert.ToInt32(key.GetValue(Kind, -1)), Value = Convert.ToInt32(key.GetValue(Value, 0)) };
                }
            }

            public void CreateBackup(PolicyValue original)
            {
                using (var root = Base()) using (var key = root.CreateSubKey(BackupPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (key == null) throw new UnauthorizedAccessException("无法创建本机策略备份注册表项。");
                    key.SetValue(Exists, original.Exists ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue(Kind, original.Exists ? (int)original.Kind : -1, RegistryValueKind.DWord);
                    key.SetValue(Value, original.Exists ? original.Value : 0, RegistryValueKind.DWord);
                    key.SetValue(Complete, 1, RegistryValueKind.DWord);
                }
            }

            public void WritePolicyValue(int value)
            {
                using (var root = Base()) using (var key = root.CreateSubKey(PolicyPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    key.SetValue(PolicyName, value, RegistryValueKind.DWord);
            }

            public void DeletePolicyValue()
            {
                using (var root = Base()) using (var key = root.OpenSubKey(PolicyPath, RegistryKeyPermissionCheck.ReadWriteSubTree))
                    if (key != null) key.DeleteValue(PolicyName, false);
            }

            public void DeleteBackup()
            {
                using (var root = Base())
                    root.DeleteSubKeyTree(BackupPath, false);
            }
        }
    }
}
