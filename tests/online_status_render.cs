using Pal98Timer;
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
class OnlineStatusRender
{
    [STAThread] static int Main(string[] args) {
        try {
            int count=0; Directory.CreateDirectory(args[0]);
            var states=new[]{"联机组件加载中","联机等待游戏","正在读取联机配置","连接中","已连接:1042","连接失败","联机构建待批准","联机组件不可用"};
            using(var sheet=new Bitmap(692,states.Length*150)) using(var canvas=Graphics.FromImage(sheet)) {
                canvas.Clear(Color.White);
                foreach(int width in new[]{270,346}) foreach(string state in states)
                using(var panel=new Panel{Size=new Size(width,width==270?200:916)}) using(var board=new GBoard()) {
                    var render=new GRender(panel); render.SetGBoard(board);render.SetTitle("自动计时器");render.SetVersion("3.38.3.6");
                    render.SetMainTimer(TimeSpan.Zero);render.SetSubTimer("0.00s");render.SetMoreInfo("");
                    render.AddBtn("隐藏",null);render.AddBtn("重置",null);render.AddBtn("功能",null);render.AddBtn("云",null);
                    render.SetGameVersion("梦幻2.2.2-普通 ["+state+"]");render.Draw();
                    var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                    var header=(Rectangle)typeof(GRender).GetField("rcGameVersion",flags).GetValue(render);
                    var dots=(Rectangle)typeof(GRender).GetField("rcDots",flags).GetValue(render);
                    var displayed=(string)typeof(GRender).GetField("gameVersionDisplay",flags).GetValue(render);
                    if(!displayed.Replace("\r","").Replace("\n","").Contains(state)||header.Bottom>dots.Top)throw new Exception("Status clipped or overlaps timer: "+state);
                    string file=Path.Combine(args[0],width+"-"+Array.IndexOf(states,state)+".png");panel.BackgroundImage.Save(file);
                    canvas.DrawImage(panel.BackgroundImage,new Rectangle(width==270?0:346,Array.IndexOf(states,state)*150,width,150),new Rectangle(0,0,width,150),GraphicsUnit.Pixel);
                    ((Graphics)typeof(GRender).GetField("CG",flags).GetValue(render)).Dispose();panel.BackgroundImage.Dispose();count++;
                }
                sheet.Save(Path.Combine(args[0],"status-sheet.png"));
            }
            Console.WriteLine("PASS "+count+" actual renderer states in normal and compact sizes; no client or game started");return 0;
        } catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
    }
}
