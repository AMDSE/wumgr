using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Microsoft.Win32;
using System.Threading;

namespace wumgr
{
    /// <summary>Native SetupAPI device enumeration. Each optional property failure is isolated.</summary>
    public static class SetupApiDeviceSource
    {
        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_ALLCLASSES = 0x00000004;
        private const uint ERROR_INSUFFICIENT_BUFFER = 122;
        private const uint ERROR_MORE_DATA = 234;
        private const uint ERROR_NO_MORE_ITEMS = 259;
        private const uint SPDRP_DEVICEDESC = 0;
        private const uint SPDRP_HARDWAREID = 1;
        private const uint SPDRP_COMPATIBLEIDS = 2;
        private const uint SPDRP_CLASS = 7;
        private const uint SPDRP_CLASSGUID = 8;
        private const uint SPDRP_FRIENDLYNAME = 12;
        private const uint SPDRP_MFG = 11;
        private const uint DICS_FLAG_GLOBAL = 1;
        private const uint DIREG_DRV = 2;
        private const int KEY_READ = 0x20019;

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        /// <summary>Expected native SP_DEVINFO_DATA size (28 on x86, 32 on x64).</summary>
        public static int NativeDeviceInfoDataSize { get { return Marshal.SizeOf(typeof(SP_DEVINFO_DATA)); } }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeDeviceInfoSetHandle SetupDiGetClassDevsW(IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiEnumDeviceInfo(SafeDeviceInfoSetHandle deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceRegistryPropertyW(SafeDeviceInfoSetHandle deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiGetDeviceInstanceIdW(SafeDeviceInfoSetHandle deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, StringBuilder instanceId, int instanceIdSize, out int requiredSize);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern SafeRegistryHandle SetupDiOpenDevRegKey(SafeDeviceInfoSetHandle deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData, uint scope, uint hwProfile, uint keyType, int samDesired);
        [DllImport("cfgmgr32.dll", SetLastError = true)]
        private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern int RegQueryValueExW(SafeRegistryHandle key, string valueName, IntPtr reserved, out uint type, StringBuilder data, ref uint dataSize);

        public sealed class SafeDeviceInfoSetHandle : SafeHandleZeroOrMinusOneIsInvalid
        {
            private SafeDeviceInfoSetHandle() : base(true) { }
            protected override bool ReleaseHandle() { return SetupDiDestroyDeviceInfoList(handle); }
        }
        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        /// <summary>Parses a Windows UTF-16 MULTI_SZ, tolerating missing final terminators.</summary>
        public static List<string> ParseMultiSz(string value)
        {
            var result = new List<string>();
            if (String.IsNullOrEmpty(value)) return result;
            string[] parts = value.Split(new[] { '\0' }, StringSplitOptions.None);
            foreach (string part in parts)
            {
                if (part.Length == 0) break;
                result.Add(part);
            }
            return result;
        }

        public static List<LocalDeviceInfo> Scan(CancellationToken cancellationToken, IProgress<string> progress)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                throw new PlatformNotSupportedException("SetupAPI 设备枚举需要 Windows。");
            var result = new List<LocalDeviceInfo>();
            using (SafeDeviceInfoSetHandle set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES))
            {
                if (set == null || set.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开设备信息集合");
                uint index = 0;
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var data = new SP_DEVINFO_DATA { cbSize = (uint)NativeDeviceInfoDataSize };
                    if (!SetupDiEnumDeviceInfo(set, index++, ref data))
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error == ERROR_NO_MORE_ITEMS) break;
                        throw new Win32Exception(error, "无法继续枚举设备");
                    }
                    try
                    {
                        LocalDeviceInfo item = ReadDevice(set, ref data);
                        if (item != null)
                        {
                            result.Add(item);
                            if (progress != null) { try { progress.Report(item.Name ?? item.InstanceId); } catch { } }
                        }
                    }
                    catch { /* A malformed or inaccessible device must not abort the scan. */ }
                }
            }
            return result;
        }

        private static LocalDeviceInfo ReadDevice(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data)
        {
            var item = new LocalDeviceInfo();
            item.InstanceId = ReadInstanceId(set, ref data);
            string friendly = ReadStringProperty(set, ref data, SPDRP_FRIENDLYNAME);
            string description = ReadStringProperty(set, ref data, SPDRP_DEVICEDESC);
            item.Name = !String.IsNullOrWhiteSpace(friendly) ? friendly : description;
            item.Manufacturer = ReadStringProperty(set, ref data, SPDRP_MFG);
            item.Class = ReadStringProperty(set, ref data, SPDRP_CLASS);
            string classGuid = ReadStringProperty(set, ref data, SPDRP_CLASSGUID);
            item.HardwareIds = ReadMultiProperty(set, ref data, SPDRP_HARDWAREID);
            item.CompatibleIds = ReadMultiProperty(set, ref data, SPDRP_COMPATIBLEIDS);
            uint status, problem;
            item.ProblemCode = CM_Get_DevNode_Status(out status, out problem, data.DevInst, 0) == 0 ? unchecked((int)problem) : -1;
            item.InstalledVersion = ReadDriverVersion(set, ref data);
            item.InstalledProvider = ReadDriverValue(set, ref data, "ProviderName");
            item.InstalledDate = NormalizeDate(ReadDriverValue(set, ref data, "DriverDate"));
            item.ClassGuid = String.IsNullOrWhiteSpace(classGuid) ? data.ClassGuid.ToString("B") : classGuid;
            if (String.IsNullOrWhiteSpace(item.Name)) item.Name = item.InstanceId;
            return item;
        }

        private static string ReadInstanceId(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data)
        {
            var sb = new StringBuilder(512); int required;
            return SetupDiGetDeviceInstanceIdW(set, ref data, sb, sb.Capacity, out required) ? sb.ToString() : String.Empty;
        }
        private static string ReadStringProperty(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data, uint property)
        {
            uint type; uint required; byte[] buffer = new byte[4096];
            if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out type, buffer, (uint)buffer.Length, out required))
            {
                if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || required == 0 || required > 1024 * 1024) return String.Empty;
                buffer = new byte[required];
                if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out type, buffer, required, out required)) return String.Empty;
            }
            int chars = Math.Min(buffer.Length / 2, (int)(required / 2));
            return Encoding.Unicode.GetString(buffer, 0, chars * 2).TrimEnd('\0');
        }
        private static List<string> ReadMultiProperty(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data, uint property)
        { return ParseMultiSz(ReadStringProperty(set, ref data, property)); }

        private static string ReadDriverVersion(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data)
        { return ReadDriverValue(set, ref data, "DriverVersion"); }

        private static string ReadDriverValue(SafeDeviceInfoSetHandle set, ref SP_DEVINFO_DATA data, string valueName)
        {
            SafeRegistryHandle key = null;
            try
            {
                key = SetupDiOpenDevRegKey(set, ref data, DICS_FLAG_GLOBAL, 0, DIREG_DRV, KEY_READ);
                if (key == null || key.IsInvalid) return String.Empty;
                for (int capacity = 256; capacity <= 1024 * 1024; capacity *= 2)
                {
                    uint type; uint size = (uint)capacity * 2;
                    var value = new StringBuilder(capacity);
                    int error = RegQueryValueExW(key, valueName, IntPtr.Zero, out type, value, ref size);
                    if (error == 0) return value.ToString().TrimEnd('\0').Trim();
                    if (error != ERROR_MORE_DATA) return String.Empty;
                }
                return String.Empty;
            }
            catch { return String.Empty; }
            finally { if (key != null) key.Dispose(); }
        }

        private static string NormalizeDate(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return String.Empty;
            DateTime date;
            string text = value.Trim();
            if (DateTime.TryParseExact(text, new[] { "yyyy-MM-dd", "yyyy/MM/dd", "MM/dd/yyyy", "M/d/yyyy",
                "MM-dd-yyyy", "M-d-yyyy", "yyyyMMdd" }, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out date))
                return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return String.Empty;
        }
    }
}
