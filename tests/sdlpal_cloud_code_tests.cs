using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
class CloudCodeTests {
 static int Main(string[] args) {
  var asm=Assembly.LoadFrom(args[0]);var type=asm.GetType("Pal98Timer.SdlpalCloudCode",true);
  var flags=BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static;
  var create=type.GetMethod("NewCode",flags);var normalize=type.GetMethod("Normalize",flags);
  int checks=0;var seen=new HashSet<string>();
  Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception(label);++checks;};
  check(((string)type.GetField("Alphabet",flags).GetRawConstantValue()).Length==60,"exact original60 alphabet");
  for(int i=0;i<10000;++i) {
   string code=(string)create.Invoke(null,null);
   check(Regex.IsMatch(code,@"\A[0-9A-Za-z]{8}\z")&&seen.Add(code),"exactly8 safe characters and distinct sample keys");
   check((string)normalize.Invoke(null,new object[]{code})==code,"generated code round trip");
  }
  string old="geshi-123-0123456789abcdef0123456789abcdef";
  check((string)normalize.Invoke(null,new object[]{old})==old,"old long code retained");
  check((string)normalize.Invoke(null,new object[]{"  ABCD1234 \r\n"})=="ABCD1234","pasted short code trimmed");
  foreach(string bad in new[]{"","1234567","123456789","../x.bin","a/bc1234","aaaa\\bbb","1234.bin","1234567\n","ＡＢＣＤ１２３４", "geshi--1-0123456789abcdef0123456789abcdef"}) {
   bool rejected=false;try{normalize.Invoke(null,new object[]{bad});}catch(TargetInvocationException e){rejected=e.InnerException is InvalidDataException;}
   check(rejected,"malformed short code rejected before transport");
  }
  File.WriteAllText(args[1],"checks="+checks+" failures=0 length=8 oldLongRead=true network=false\n");
  return 0;
 }
}
