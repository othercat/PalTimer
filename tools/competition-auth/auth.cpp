#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <bcrypt.h>
#include <wincrypt.h>
#include <objbase.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <vector>
#include "release.generated.h"
#pragma comment(lib,"bcrypt.lib")
#pragma comment(lib,"crypt32.lib")
#pragma comment(lib,"advapi32.lib")
#pragma comment(lib,"ole32.lib")

namespace {
HMODULE moduleHandle;
const std::string protocol = "PAL98.TimerUploadAuth.v2";
using Bytes = std::vector<unsigned char>;
void require(bool value) { if (!value) throw std::runtime_error("invalid"); }
void nt(NTSTATUS status) { require(status >= 0); }
struct Algorithm {
    BCRYPT_ALG_HANDLE value = nullptr;
    explicit Algorithm(LPCWSTR id) { nt(BCryptOpenAlgorithmProvider(&value,id,nullptr,0)); }
    ~Algorithm() { if(value) BCryptCloseAlgorithmProvider(value,0); }
};
struct Hash {
    Algorithm algorithm{BCRYPT_SHA256_ALGORITHM};
    BCRYPT_HASH_HANDLE value = nullptr;
    Bytes object;
    Hash() {
        DWORD length=0, got=0;
        nt(BCryptGetProperty(algorithm.value,BCRYPT_OBJECT_LENGTH,reinterpret_cast<PUCHAR>(&length),sizeof(length),&got,0));
        object.resize(length);
        nt(BCryptCreateHash(algorithm.value,&value,object.data(),length,nullptr,0,0));
    }
    ~Hash() { if(value) BCryptDestroyHash(value); }
    void add(const void* bytes,size_t count) { require(count<=0xffffffffu); nt(BCryptHashData(value,(PUCHAR)bytes,static_cast<ULONG>(count),0)); }
    Bytes finish() { Bytes result(32); nt(BCryptFinishHash(value,result.data(),32,0)); return result; }
};
Bytes digest(const std::string& s) { Hash h; h.add(s.data(),s.size()); return h.finish(); }
std::string hex(const Bytes& b) {
    const char* symbols="0123456789abcdef"; std::string out; out.reserve(b.size()*2);
    for(auto c:b) { out+=symbols[c>>4]; out+=symbols[c&15]; } return out;
}
bool lowerHex(const std::string& s,size_t count) { return s.size()==count && s.find_first_not_of("0123456789abcdef")==std::string::npos; }
std::wstring filePath(HMODULE m) {
    std::vector<wchar_t> path(32768); DWORD count=GetModuleFileNameW(m,path.data(),static_cast<DWORD>(path.size()));
    require(count>0 && count<path.size()-1); std::wstring p(path.data(),count);
    if(p.rfind(L"\\\\?\\",0)==0) return p;
    return p.rfind(L"\\\\",0)==0 ? L"\\\\?\\UNC\\"+p.substr(2) : L"\\\\?\\"+p;
}
std::string fileDigestPath(const std::wstring& path) {
    HANDLE file=CreateFileW(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_FLAG_SEQUENTIAL_SCAN,nullptr);
    require(file!=INVALID_HANDLE_VALUE);
    try {
        Hash h; Bytes buffer(512*1024); DWORD got=0;
        for(;;) { require(ReadFile(file,buffer.data(),static_cast<DWORD>(buffer.size()),&got,nullptr)!=FALSE); if(!got) break; h.add(buffer.data(),got); }
        auto result=hex(h.finish()); CloseHandle(file); return result;
    } catch(...) { CloseHandle(file); throw; }
}
std::string fileDigest(HMODULE m) { return fileDigestPath(filePath(m)); }
void validateOnlineComponent() {
    if (!ReleaseOnlineSha256[0]) return; // Legacy fixture/build, never a modern release.
    auto host=filePath(nullptr); auto slash=host.find_last_of(L"\\/"); require(slash!=std::wstring::npos);
    require(fileDigestPath(host.substr(0,slash+1)+L"PalTimerOnline.dll")==ReleaseOnlineSha256);
}
std::string base64(const Bytes& data) {
    DWORD size=0; require(CryptBinaryToStringA(data.data(),static_cast<DWORD>(data.size()),CRYPT_STRING_BASE64|CRYPT_STRING_NOCRLF,nullptr,&size)!=FALSE);
    std::string out(size,'\0'); require(CryptBinaryToStringA(data.data(),static_cast<DWORD>(data.size()),CRYPT_STRING_BASE64|CRYPT_STRING_NOCRLF,&out[0],&size)!=FALSE);
    out.resize(size); if(!out.empty() && out.back()=='\0') out.pop_back(); return out;
}
std::string base64(const std::string& s) { return base64(Bytes(s.begin(),s.end())); }
std::string decode64(const std::string& text,size_t max) {
    require(!text.empty() && text.size()<=max*2 && text.find_first_not_of("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=")==std::string::npos);
    DWORD size=0; require(CryptStringToBinaryA(text.c_str(),static_cast<DWORD>(text.size()),CRYPT_STRING_BASE64,nullptr,&size,nullptr,nullptr)!=FALSE && size<=max);
    Bytes bytes(size); require(CryptStringToBinaryA(text.c_str(),static_cast<DWORD>(text.size()),CRYPT_STRING_BASE64,bytes.data(),&size,nullptr,nullptr)!=FALSE);
    bytes.resize(size); require(base64(bytes)==text); return std::string(bytes.begin(),bytes.end());
}
std::string sign(const std::string& input) {
    Algorithm algorithm(BCRYPT_RSA_ALGORITHM); BCRYPT_KEY_HANDLE key=nullptr;
    nt(BCryptImportKeyPair(algorithm.value,nullptr,BCRYPT_RSAPRIVATE_BLOB,&key,(PUCHAR)ReleasePrivateBlob,sizeof(ReleasePrivateBlob),0));
    try {
        auto hash=digest(input); BCRYPT_PKCS1_PADDING_INFO padding{BCRYPT_SHA256_ALGORITHM}; DWORD size=0;
        nt(BCryptSignHash(key,&padding,hash.data(),static_cast<ULONG>(hash.size()),nullptr,0,&size,BCRYPT_PAD_PKCS1));
        Bytes signature(size); nt(BCryptSignHash(key,&padding,hash.data(),static_cast<ULONG>(hash.size()),signature.data(),size,&size,BCRYPT_PAD_PKCS1));
        BCryptDestroyKey(key); signature.resize(size); return base64(signature);
    } catch(...) { BCryptDestroyKey(key); throw; }
}
std::string utf8(const std::wstring& s) {
    int size=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),static_cast<int>(s.size()),nullptr,0,nullptr,nullptr); require(size>0);
    std::string out(size,'\0'); require(WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,s.data(),static_cast<int>(s.size()),&out[0],size,nullptr,nullptr)==size); return out;
}
std::string hardwareId() {
    HKEY key=nullptr; require(RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\Microsoft\\Cryptography",0,KEY_READ|KEY_WOW64_64KEY,&key)==ERROR_SUCCESS);
    wchar_t value[128]{}; DWORD bytes=sizeof(value),type=0;
    LONG status=RegQueryValueExW(key,L"MachineGuid",nullptr,&type,reinterpret_cast<LPBYTE>(value),&bytes); RegCloseKey(key);
    require(status==ERROR_SUCCESS && type==REG_SZ && bytes<sizeof(value));
    std::wstring guid=value; if(guid.size()==36) guid=L"{"+guid+L"}";
    GUID parsed{}; require(CLSIDFromString(guid.c_str(),&parsed)==S_OK);
    wchar_t normalized[40]{}; require(StringFromGUID2(parsed,normalized,40)==39);
    std::string canonical=utf8(std::wstring(normalized+1,36));
    std::transform(canonical.begin(),canonical.end(),canonical.begin(),[](char c){return c>='A'&&c<='F'?char(c+32):c;});
    return "win-"+hex(digest("PAL98.TimerDevice.v1\n"+canonical));
}
// Bounded strict JSON parser: rejects duplicate names, invalid UTF-8, excessive
// nesting and non-integer numbers. This ABI only accepts the fixed run contract.
struct Json {
    char type='n'; std::string text; std::map<std::string,Json> fields; std::vector<Json> items;
    const Json& at(const char* name) const { require(type=='o'); auto it=fields.find(name); require(it!=fields.end()); return it->second; }
    std::string str(const char* name) const { auto& v=at(name); require(v.type=='s'); return v.text; }
    long long integer() const { require(type=='i'); size_t used=0; auto value=std::stoll(text,&used); require(used==text.size()); return value; }
};
class Parser {
    const std::string& s; size_t p=0;
    void ws() { while(p<s.size() && (s[p]==' '||s[p]=='\r'||s[p]=='\n'||s[p]=='\t')) ++p; }
    char take() { require(p<s.size()); return s[p++]; }
    unsigned unicode() { unsigned v=0; for(int i=0;i<4;i++){char c=take();require((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F'));v=(v<<4)|(c<='9'?c-'0':(c&~32)-'A'+10);}return v; }
    std::string stringValue() {
        require(take()=='"'); std::string out;
        while(true) {
            char c=take(); if(c=='"') return out; require(static_cast<unsigned char>(c)>=32);
            if(c!='\\') {out+=c;continue;} c=take();
            if(c=='"'||c=='\\'||c=='/') out+=c;
            else if(c=='b')out+='\b';else if(c=='f')out+='\f';else if(c=='n')out+='\n';else if(c=='r')out+='\r';else if(c=='t')out+='\t';
            else if(c=='u') {
                unsigned v=unicode(); std::wstring w(1,static_cast<wchar_t>(v));
                if(v>=0xd800 && v<=0xdbff){require(take()=='\\'&&take()=='u');unsigned low=unicode();require(low>=0xdc00&&low<=0xdfff);w+=static_cast<wchar_t>(low);}
                else require(v<0xdc00||v>0xdfff);
                out+=utf8(w);
            } else require(false);
        }
    }
    Json value(int depth) {
        require(depth<=16);ws();require(p<s.size());Json j;char c=s[p];
        if(c=='"'){j.type='s';j.text=stringValue();return j;}
        if(c=='{'||c=='['){++p;j.type=c=='{'?'o':'a';ws();char end=c=='{'?'}':']';if(p<s.size()&&s[p]==end){++p;return j;}
            for(;;){if(j.type=='o'){ws();auto name=stringValue();ws();require(take()==':');auto child=value(depth+1);require(j.fields.emplace(name,std::move(child)).second);}
                else {require(j.items.size()<256);j.items.push_back(value(depth+1));}
                ws();char next=take();if(next==end)return j;require(next==',');}
        }
        if(c=='n'){require(s.compare(p,4,"null")==0);p+=4;return j;}
        if(c=='t'||c=='f'){const char* literal=c=='t'?"true":"false";size_t count=c=='t'?4:5;require(s.compare(p,count,literal)==0);p+=count;j.type='b';j.text=literal;return j;}
        size_t begin=p;if(c=='-')++p;require(p<s.size()&&s[p]>='0'&&s[p]<='9');if(s[p]=='0')++p;else while(p<s.size()&&s[p]>='0'&&s[p]<='9')++p;
        j.type='i';j.text=s.substr(begin,p-begin);j.integer();return j;
    }
public:
    explicit Parser(const std::string& text):s(text){}
    Json parse(){require(!s.empty()&&s.size()<=98304);require(MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,s.data(),static_cast<int>(s.size()),nullptr,0)>0);auto j=value(0);ws();require(p==s.size());return j;}
};
std::string quote(const std::string& s) {
    std::string out="\"";const char* symbols="0123456789abcdef";
    for(unsigned char c:s){if(c=='"'||c=='\\'){out+='\\';out+=c;}else if(c<32){out+="\\u00";out+=symbols[c>>4];out+=symbols[c&15];}else out+=c;}return out+'"';
}
using Fields=std::map<std::string,std::string>;
std::string json(const Fields& fields){std::string out="{";for(auto& f:fields){if(out.size()>1)out+=',';out+=quote(f.first)+":"+quote(f.second);}return out+'}';}
void exact(const Json& j,std::initializer_list<const char*> names){require(j.type=='o'&&j.fields.size()==names.size());for(auto n:names)j.at(n);}
bool identifier(const std::string& s,size_t max){return !s.empty()&&static_cast<size_t>(std::count_if(s.begin(),s.end(),[](unsigned char c){return (c&0xc0)!=0x80;}))<=max&&std::all_of(s.begin(),s.end(),[](unsigned char c){return c>32&&c!=127;});}
bool guid(const std::string& s){if(s.size()!=36)return false;for(size_t i=0;i<s.size();i++){if(i==8||i==13||i==18||i==23){if(s[i]!='-')return false;}else if(std::string("0123456789abcdef").find(s[i])==std::string::npos)return false;}return true;}
void scope(const std::string& origin,const std::string& event){
    require(origin.size()<=200&&identifier(origin,200)&&origin.back()!='/'&&origin.find_first_of("\"\\?#@") == std::string::npos);
    bool https=origin.rfind("https://",0)==0;
    bool loop=origin.rfind("http://127.0.0.1:",0)==0||origin.rfind("http://localhost:",0)==0;
    require(https||loop);size_t begin=https?8:7;require(origin.find('/',begin)==std::string::npos&&origin.size()>begin);
    require(event=="timer-online");
}
Fields identity(){
    require(fileDigest(nullptr)==ReleaseExeSha256); validateOnlineComponent();
    return {{"protocol",protocol},{"key_id",ReleaseKeyId},{"timer_exe_sha256",ReleaseExeSha256},{"timer_version",ReleaseTimerVersion},{"component_sha256",fileDigest(moduleHandle)}};
}
std::string input(const Fields& f,bool run){
    std::vector<std::string> names={"protocol","kind","key_id","timer_exe_sha256","timer_version","component_sha256","server_origin","scope","hwid"};
    if(run){names.push_back("run_id");names.push_back("payload_sha256");}else{names.push_back("challenge_id");names.push_back("nonce");names.push_back("request_sha256");}
    std::string out;for(auto& n:names){auto v=f.at(n);require(v.find_first_of("\r\n")==std::string::npos);out+=v+"\n";}return out;
}

size_t characterCount(const std::string& text) { return static_cast<size_t>(std::count_if(text.begin(),text.end(),[](unsigned char c){return (c&0xc0)!=0x80;})); }
bool nullableText(const Json& value,size_t maximum) { return value.type=='n'||(value.type=='s'&&characterCount(value.text)<=maximum); }
void gameplayV2(const Json& g) {
    if(g.type=='n')return;
    Json required=g;
    auto walk=required.fields.find("walk_frame_ms");
    if(walk!=required.fields.end()) {
        if(walk->second.type!='n') { auto value=walk->second.integer();require(value>=0&&value<=1000000); }
        required.fields.erase(walk);
    }
    for(auto name:{"speed","battle_speed"}) {
        auto field=required.fields.find(name);
        if(field!=required.fields.end()) {
            if(field->second.type!='n') { auto value=field->second.integer();require(value>=0&&value<=100); }
            required.fields.erase(field);
        }
    }
    exact(required,{"schema","rules_sha256","content_id","content_sha256","family","fade_ms","map_speed_ticks"});
    require(g.str("schema")=="PAL98.GameplayIdentity.v1");
    for(auto name:{"rules_sha256","content_id","content_sha256","family"}) require(nullableText(g.at(name),160));
    require(g.at("fade_ms").integer()>=0&&g.at("fade_ms").integer()<=86400000);
    require(g.at("map_speed_ticks").integer()>=0&&g.at("map_speed_ticks").integer()<=1000000);
}
void hardcoreV2(const Json& h) {
    if(h.type=='n')return;
    exact(h,{"requested","run_verified","rules_version","evidence_status"});
    require(h.at("requested").type=='b'&&h.at("run_verified").type=='b');
    require(h.at("rules_version").integer()>=0&&h.at("rules_version").integer()<=1000000);
    require(nullableText(h.at("evidence_status"),160));
}
void rankingV2(const Json& r) {
    if(r.type=='n')return;
    exact(r,{"schema","configuration_id","covered","rules"});
    require(r.str("schema")=="PAL98.RankingConfiguration.v1"&&r.at("covered").type=='b');
    require(nullableText(r.at("configuration_id"),160));
    auto& rules=r.at("rules"); if(rules.type=='n')return;
    require(rules.type=='o'&&rules.fields.size()<=512);
    for(auto& f:rules.fields) require(!f.first.empty()&&f.first.size()<=160&&
        f.first.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-")==std::string::npos&&
        nullableText(f.second,2048));
}
Json validateRunV2(const Json& run,const std::string& hwid,bool hostVersion) {
    // Old sealed payloads may omit restart_ms; retain their original bytes.
    // Validate the optional field without relaxing the remaining exact schema.
    Json required=run;
    auto restart=required.fields.find("restart_ms");
    if(restart!=required.fields.end()) {
        if(restart->second.type!='n') { auto value=restart->second.integer();require(value>=0&&value<=86400000); }
        required.fields.erase(restart);
    }
    auto revision=required.fields.find("release_revision");
    if(revision!=required.fields.end()) {
        if(revision->second.type!='n') {
            require(revision->second.type=='s');const auto& value=revision->second.text;
            require(value.size()>=2&&value.size()<=7&&value[0]=='r'&&value[1]>='1'&&value[1]<='9'&&
                value.find_first_not_of("0123456789",1)==std::string::npos);
        }
        required.fields.erase(revision);
    }
    exact(required,{"protocol","run_id","hwid","route_sha256","started_at","finished_at","total_ms","timer_version","game_version","pal_dll_sha256","splits","timeline_id","gameplay","hardcore","ranking","custom_competition_id","observations"});
    require(run.str("hwid")==hwid&&guid(run.str("run_id"))&&(!hostVersion||run.str("timer_version")==ReleaseTimerVersion));
    require(identifier(run.str("timer_version"),40)&&identifier(run.str("started_at"),50)&&identifier(run.str("finished_at"),50));
    require(nullableText(run.at("game_version"),40));
    for(auto name:{"pal_dll_sha256","timeline_id"})require(run.at(name).type=='n'||(run.at(name).type=='s'&&lowerHex(run.at(name).text,64)));
    auto total=run.at("total_ms").integer();require(total>=0&&total<=86400000);
    auto& custom=run.at("custom_competition_id");require(custom.type=='n'||(custom.type=='s'&&!custom.text.empty()&&custom.text.size()<=60&&custom.text.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-")==std::string::npos));
    gameplayV2(run.at("gameplay")); rankingV2(run.at("ranking")); hardcoreV2(run.at("hardcore"));
    auto& o=run.at("observations"); exact(o,{"began_here","finished","issues","initial_gameplay","initial_ranking","initial_hardcore"});
    require(o.at("began_here").type=='b'&&o.at("finished").type=='b');
    auto& issues=o.at("issues");require(issues.type=='a'&&issues.items.size()<=32);
    for(auto& issue:issues.items)require(issue.type=='s'&&characterCount(issue.text)<=300);
    gameplayV2(o.at("initial_gameplay"));rankingV2(o.at("initial_ranking"));hardcoreV2(o.at("initial_hardcore"));
    auto& splits=run.at("splits");require(splits.type=='a'&&!splits.items.empty()&&splits.items.size()<=256);
    std::string route="PAL98.TimerRoute.v1\n";std::set<std::string> ids;
    for(auto& split:splits.items){
        exact(split,{"checkpoint_id","elapsed_ms","status"});auto id=split.str("checkpoint_id");
        require(identifier(id,80)&&ids.insert(id).second);route+=id+"\n";
        auto status=split.str("status");
        if(status=="completed"){auto elapsed=split.at("elapsed_ms").integer();require(elapsed>=0&&elapsed<=86400000);}
        else require((status=="auto_skipped"||status=="manual_skipped"||status=="in_progress")&&split.at("elapsed_ms").type=='n');
    }
    require(hex(digest(route))==run.str("route_sha256"));return run;
}
Json validateRun(const std::string& payload,const std::string& hwid,bool hostVersion){
    require(payload.size()<=65536);auto run=Parser(payload).parse();
    const auto version=run.str("protocol");
    if(version=="PAL98.TimerOnline.v2")return validateRunV2(run,hwid,hostVersion);
    {
        require(version=="PAL98.TimerOnline.v1");
        exact(run,{"protocol","run_id","hwid","route_sha256","started_at","finished_at","total_ms","timer_version","game_version","pal_dll_sha256","splits","timeline_id","gameplay","hardcore","ranking","custom_competition_id"});
        const auto& g=run.at("gameplay"); const auto& h=run.at("hardcore");
        exact(g,{"schema","rules_sha256","content_id","content_sha256","family","fade_ms","map_speed_ticks"});
        exact(h,{"requested","run_verified","rules_version","evidence_status"});
        require(g.str("schema")=="PAL98.GameplayIdentity.v1"&&lowerHex(g.str("rules_sha256"),64)&&
            lowerHex(g.str("content_sha256"),64)&&identifier(g.str("content_id"),80));
        require(g.str("family")=="standard"||g.str("family")=="drawcard");
        const auto fade=g.at("fade_ms").integer(), speed=g.at("map_speed_ticks").integer();
        require((fade==1200||fade==800)&&(speed==10||speed==9));
        const auto& ranking=run.at("ranking"); exact(ranking,{"schema","configuration_id","covered","rules"});
        require(ranking.str("schema")=="PAL98.RankingConfiguration.v1"&&ranking.at("covered").type=='b'&&ranking.at("covered").text=="true");
        const auto& rules=ranking.at("rules"); require(rules.type=='o'&&!rules.fields.empty()&&rules.fields.size()<=512);
        std::string canonical="PAL98.RankingConfiguration.v1\n";
        for(const auto& f:rules.fields){ require(!f.first.empty()&&f.first.size()<=160&&f.first.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.-")==std::string::npos&&f.second.type=='s'&&f.second.text.size()<=2048); canonical+=quote(f.first)+":"+quote(f.second.text)+"\n"; }
        require(ranking.str("configuration_id")==hex(digest(canonical))&&rules.str("content")==g.str("content_sha256")&&rules.str("family")==g.str("family")&&rules.str("fade_ms")==std::to_string(fade)&&rules.str("map_speed_ticks")==std::to_string(speed));
        const auto& custom=run.at("custom_competition_id");require(custom.type=='n'||(custom.type=='s'&&!custom.text.empty()&&custom.text.size()<=60&&custom.text.find_first_not_of("abcdefghijklmnopqrstuvwxyz0123456789-")==std::string::npos));
        require(h.at("requested").type=='b'&&h.at("run_verified").type=='b');
        bool requested=h.at("requested").text=="true", verified=h.at("run_verified").text=="true";
        auto revision=h.at("rules_version").integer();auto status=h.str("evidence_status");
        require(!requested ? !verified&&revision==0&&status=="ordinary" :
            revision>=0&&revision<=4&&(verified ? revision>=1&&status=="runtime_observed" : status=="unverified"));
        std::string timeline="PAL98.TimelineIdentity.v1\n"+g.str("rules_sha256")+"\n"+run.str("route_sha256")+"\n"+(requested?"hardcore":"ordinary")+"\n";
        require(run.str("timeline_id")==hex(digest(timeline)));
    }
    require(run.str("hwid")==hwid&&guid(run.str("run_id")));
    require(!hostVersion||run.str("timer_version")==ReleaseTimerVersion);
    require(lowerHex(run.str("route_sha256"),64)&&lowerHex(run.str("pal_dll_sha256"),64));
    require(identifier(run.str("timer_version"),40)&&identifier(run.str("game_version"),40)&&identifier(run.str("started_at"),50)&&identifier(run.str("finished_at"),50));
    auto total=run.at("total_ms").integer();require(total>0&&total<=86400000);
    auto& splits=run.at("splits");require(splits.type=='a'&&!splits.items.empty()&&splits.items.size()<=256);long long last=-1;std::string route="PAL98.TimerRoute.v1\n";std::set<std::string> ids;
    for(auto& split:splits.items){exact(split,{"checkpoint_id","elapsed_ms","status"});auto id=split.str("checkpoint_id");require(identifier(id,80)&&ids.insert(id).second);route+=id+"\n";
        auto status=split.str("status");if(status=="completed"){auto elapsed=split.at("elapsed_ms").integer();require(elapsed>=0&&elapsed>=last&&elapsed<=86400000);last=elapsed;}
        else require((status=="auto_skipped"||status=="manual_skipped")&&split.at("elapsed_ms").type=='n');}
    require(splits.items.back().str("status")=="completed"&&hex(digest(route))==run.str("route_sha256"));return run;
}
std::string seal(const std::string& origin,const std::string& event,const std::string& payload){
    scope(origin,event);auto f=identity();auto hwid=hardwareId();auto run=validateRun(payload,hwid,true);
    f["kind"]="run";f["server_origin"]=origin;f["scope"]=event;f["hwid"]=hwid;f["run_id"]=run.str("run_id");f["payload_sha256"]=hex(digest(payload));
    f["signature_base64"]=sign(input(f,true));f.erase("payload_sha256");f["payload_base64"]=base64(payload);return json(f);
}
std::string prove(const std::string& sealed,const std::string& challenge){
    auto f=identity();auto s=Parser(sealed).parse();auto c=Parser(challenge).parse();
    exact(s,{"protocol","kind","key_id","timer_exe_sha256","timer_version","component_sha256","server_origin","scope","hwid","run_id","payload_base64","signature_base64"});
    exact(c,{"protocol","scope","server_origin","hwid","key_id","timer_exe_sha256","challenge_id","nonce","request_sha256","expires_at"});
    auto origin=c.str("server_origin"),event=c.str("scope"),hwid=hardwareId();scope(origin,event);
    require(s.str("protocol")==protocol&&s.str("kind")=="run"&&c.str("protocol")==protocol&&s.str("server_origin")==origin&&s.str("scope")==event&&s.str("hwid")==hwid&&c.str("hwid")==hwid);
    auto run=validateRun(decode64(s.str("payload_base64"),65536),hwid,false);require(run.str("run_id")==s.str("run_id")&&run.str("timer_version")==s.str("timer_version"));
    require(lowerHex(s.str("key_id"),32)&&lowerHex(s.str("timer_exe_sha256"),64)&&lowerHex(s.str("component_sha256"),64)&&decode64(s.str("signature_base64"),256).size()==256);
    require(c.str("key_id")==ReleaseKeyId&&c.str("timer_exe_sha256")==ReleaseExeSha256&&guid(c.str("challenge_id"))&&lowerHex(c.str("nonce"),64)&&c.str("request_sha256")==hex(digest(sealed)));
    f["kind"]="request";f["server_origin"]=origin;f["scope"]=event;f["hwid"]=hwid;f["challenge_id"]=c.str("challenge_id");f["nonce"]=c.str("nonce");f["request_sha256"]=hex(digest(sealed));
    f["signature_base64"]=sign(input(f,false));return json(f);
}
// A distinct, fixed-purpose challenge proves an approved host once per live
// connection. Heartbeats use the resulting short-lived server session, so the
// timer never re-hashes its EXE or performs RSA work on every timing tick.
std::string proveLive(const std::string& challenge){
    auto c=Parser(challenge).parse();
    exact(c,{"protocol","scope","server_origin","challenge_id","nonce","expires_at","key_id","timer_exe_sha256","hwid","instance_id"});
    auto origin=c.str("server_origin"),purpose=c.str("scope"),hwid=hardwareId();scope(origin,purpose);
    require(c.str("protocol")=="PAL98.TimerLive.v1"&&c.str("hwid")==hwid&&c.str("key_id")==ReleaseKeyId&&c.str("timer_exe_sha256")==ReleaseExeSha256);
    require(guid(c.str("instance_id"))&&guid(c.str("challenge_id"))&&lowerHex(c.str("nonce"),64));
    FILETIME ft{};GetSystemTimeAsFileTime(&ft);ULARGE_INTEGER ticks{};ticks.LowPart=ft.dwLowDateTime;ticks.HighPart=ft.dwHighDateTime;
    auto now=static_cast<long long>((ticks.QuadPart-116444736000000000ULL)/10000000ULL);
    auto expiry=c.at("expires_at").integer();require(expiry>now&&expiry<=now+180);
    auto f=identity();f["protocol"]="PAL98.TimerLive.v1";f["kind"]="session";f["server_origin"]=origin;f["scope"]=purpose;f["hwid"]=hwid;
    f["instance_id"]=c.str("instance_id");f["challenge_id"]=c.str("challenge_id");f["nonce"]=c.str("nonce");
    std::string canonical;
    for(const char* name:{"protocol","kind","key_id","timer_exe_sha256","timer_version","component_sha256","server_origin","scope","hwid","instance_id","challenge_id","nonce"}){
        auto value=f.at(name);require(value.find_first_of("\r\n")==std::string::npos);canonical+=value+"\n";
    }
    f["signature_base64"]=sign(canonical);return json(f);
}
std::string bytes(const unsigned char* p,DWORD count,DWORD max){require(p&&count>0&&count<=max);return std::string(reinterpret_cast<const char*>(p),count);}
DWORD output(const std::string& value,unsigned char* out,DWORD capacity){require(out&&capacity>value.size());std::memcpy(out,value.data(),value.size());out[value.size()]=0;return 0;}
}
extern "C" __declspec(dllexport) DWORD __stdcall PCA_GetIdentity(unsigned char* out,DWORD capacity){try{return output(json(identity()),out,capacity);}catch(...){return 1;}}
extern "C" __declspec(dllexport) DWORD __stdcall PCA_SealRun(const char* origin,const char* event,const unsigned char* payload,DWORD count,unsigned char* out,DWORD capacity){try{require(origin&&event);return output(seal(origin,event,bytes(payload,count,65536)),out,capacity);}catch(...){return 2;}}
extern "C" __declspec(dllexport) DWORD __stdcall PCA_ProveUpload(const unsigned char* sealed,DWORD sealCount,const unsigned char* challenge,DWORD challengeCount,unsigned char* out,DWORD capacity){try{return output(prove(bytes(sealed,sealCount,98304),bytes(challenge,challengeCount,16384)),out,capacity);}catch(...){return 3;}}
extern "C" __declspec(dllexport) DWORD __stdcall PCA_ProveLiveSession(const unsigned char* challenge,DWORD count,unsigned char* out,DWORD capacity){try{return output(proveLive(bytes(challenge,count,16384)),out,capacity);}catch(...){return 4;}}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH){moduleHandle=instance;DisableThreadLibraryCalls(instance);}return TRUE;}
