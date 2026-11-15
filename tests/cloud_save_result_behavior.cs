// Refs #61: exercise real controls without showing UI or touching the clipboard.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Pal98Timer;

internal static class CloudSaveResultBehavior
{
    static int checks;
    static void Check(bool value,string label){++checks;if(!value)throw new Exception(label);}
    [STAThread] static int Main(string[] args)
    {
        try {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using(var old=new Upload(null)) {
                Check(old.txtStatus.ReadOnly,"traditional upload code remains read-only");
                Check(old.Controls.Find("btnCopy",true).Length==0,"traditional constructor layout unchanged");
            }
            foreach(string code in new[]{"01234567","a1B2c3D4"})using(var dialog=new Upload(null)) {
                typeof(Upload).GetMethod("SetCompletedCode",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dialog,new object[]{code});
                Check(dialog.txtStatus.Text==code&&dialog.txtStatus.ReadOnly&&dialog.txtStatus.ShortcutsEnabled,"exact code is selectable with native shortcuts");
                dialog.txtStatus.SelectAll();
                Check(dialog.txtStatus.SelectedText==code,"full eight-character code can be selected directly");
                var copy=dialog.Controls.Find("btnCopy",true).Single() as Button;
                Check(copy!=null&&copy.Text=="复制"&&copy.Enabled,"visible enabled copy button exists");
                Check(copy.Parent.ClientRectangle.Contains(copy.Bounds)&&!copy.Bounds.IntersectsWith(dialog.btnOK.Bounds),"copy and close buttons fit without overlap");
                // Show only an invisible window so native child controls are
                // realized for WM_PRINT/DrawToBitmap. No desktop interaction.
                dialog.Opacity=0;dialog.Show();Application.DoEvents();
                using(var bitmap=new Bitmap(dialog.Width,dialog.Height)) {
                    dialog.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                    using(var preview=new Bitmap(bitmap.Width*3,bitmap.Height*3))using(var drawing=Graphics.FromImage(preview)) {
                        drawing.InterpolationMode=InterpolationMode.NearestNeighbor;drawing.PixelOffsetMode=PixelOffsetMode.Half;
                        drawing.DrawImage(bitmap,new Rectangle(Point.Empty,preview.Size));
                        preview.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"cloud-code-"+code+".png"));
                    }
                }
                dialog.Hide();
            }
            Console.WriteLine("PASS "+checks+" actual result controls; no visible windows, clipboard writes or network");return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
