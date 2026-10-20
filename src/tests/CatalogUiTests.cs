using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using wumgr;
class CatalogUiTests
{
    static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(o);}
    static void Check(bool b,string s){if(!b)throw new Exception(s);Console.WriteLine("通过："+s);}
    [STAThread] public static int Main()
    {
        try
        {
            UiLanguageService.Set(UiLanguage.ZhCN,false);
            using(var form=new DriverForm(false))
            {
                var item=new DriverInfo{Id="027e8668-e62d-411f-a4a8-7ad2a44d8d09",Source="Microsoft Update Catalog",CatalogHardwareVerified=true,DeviceModel="目录测试网卡",HardwareId="PCI\\VEN_8086&DEV_15F3&REV_01",DriverClass="Net",Version="2.1.5.10"};
                Field<List<LocalDeviceInfo>>(form,"localDevices").Add(new LocalDeviceInfo{InstanceId="CATALOG_TEST",Name="目录测试网卡",Class="Net",HardwareIds=new List<string>{item.HardwareId}});
                Field<List<DriverInfo>>(form,"drivers").Add(item);
                form.Show(); form.GetType().GetMethod("RefreshRows",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,null); Application.DoEvents();
                var card=Field<FlowLayoutPanel>(form,"list").Controls.Cast<Control>().Single();
                Check(card.Controls.OfType<CheckBox>().Single().Enabled,"已核验目录项即使尚未解析URL也可勾选");
                Field<Button>(form,"selectAll").PerformClick();Application.DoEvents();
                Check(Field<HashSet<string>>(form,"selected").Contains(item.Id)&&Field<Button>(form,"download").Enabled,"全选与主下载按钮可进入目录延迟解析流程");
                Check(!Field<CheckBox>(form,"silentInstall").Checked,"目录集成保持默认仅下载");
                using(var dialog=new DeviceSearchForm(Field<List<LocalDeviceInfo>>(form,"localDevices"),new List<DriverInfo>{item}))
                {
                    dialog.Show();Application.DoEvents();
                    Check(Field<Button>(dialog,"download").Enabled,"设备搜索目录项可下载");
                    var grid=Field<DataGridView>(dialog,"driverGrid");
                    Check(grid.Columns.Cast<DataGridViewColumn>().Any(x=>x.DataPropertyName=="Source")&&grid.Columns.Cast<DataGridViewColumn>().Any(x=>x.DataPropertyName=="Architecture"),"设备搜索显示来源与架构");
                    dialog.Close();
                }
                form.Close();
            }
            Console.WriteLine("目录界面测试完成：5 项全部通过。");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
