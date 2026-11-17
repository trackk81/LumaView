using System;using System.IO;using System.Threading;using System.Reflection;
[assembly:AssemblyProduct("LumaView")]
#if OLD
[assembly:AssemblyFileVersion("1.0.0.0")]
#else
[assembly:AssemblyFileVersion("1.0.1.0")]
#endif
class Fixture {
 static void Main(string[] args){
#if OLD
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"old-restarted.txt"),"yes");
#elif FAIL
  Thread.Sleep(300);
#else
  if(args.Length>=2&&args[0]=="--update-complete")using(var ready=EventWaitHandle.OpenExisting("Local\\LumaView.Update."+args[1])){ready.Set();}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"new-started.txt"),String.Join("|",args));Thread.Sleep(800);
#endif
 }
}
