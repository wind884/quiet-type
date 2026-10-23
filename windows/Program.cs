using System;
using System.Reflection;
using System.Windows.Forms;
[assembly: AssemblyTitle("轻打")]
[assembly: AssemblyDescription("Typing practice")]
[assembly: AssemblyProduct("Quiet Type")]
namespace QuietType
{
    static class Program
    {
        [STAThread] static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
