#include "texture-search.h"
#include <assimp/GltfMaterial.h>
#include <windows.h>
#include <algorithm>
#include <cwctype>
#include <set>

namespace fs=std::filesystem;
using Name=std::wstring;
using Paths=std::vector<fs::path>;

static Name lower(Name s){ for(auto& c:s)c=std::towlower(c);return s; }
static Name compact(const Name& s){
    Name out;
    for(auto c:lower(s))if(std::iswalnum(c)||c>=128)out+=c;
    return out;
}
static bool suffix(const Name& s,const Name& end){return s.size()>=end.size()&&s.compare(s.size()-end.size(),end.size(),end)==0;}
static bool image(const fs::path& p){
    static const std::set<Name> formats={L".png",L".jpg",L".jpeg",L".bmp",L".tga",L".webp",L".psd",L".psb",L".tif",L".tiff",L".dds",L".ktx2",L".hdr",L".exr",L".gif",L".avif"};
    return formats.count(lower(p.extension().wstring()))!=0;
}
static Name stem(const fs::path& p){
    auto base=p.stem();
    if(image(base))base=base.stem(); // atlas.tga.png
    return compact(base.wstring());
}
static bool packageDir(const fs::path& path){
    auto s=lower(path.filename().wstring());
    return s==L"texture"||s==L"textures"||s==L"maps"||s==L"materials"||s==L"images"||s==L"sourceimages"||suffix(s,L".fbm");
}
static bool link(const fs::path& path){
    auto flags=GetFileAttributesW(path.c_str());
    return flags==INVALID_FILE_ATTRIBUTES||(flags&FILE_ATTRIBUTE_REPARSE_POINT)!=0;
}
static void scan(const fs::path& dir,Paths& files,bool descend){
    std::error_code ec;
    for(fs::directory_iterator it(dir,fs::directory_options::skip_permission_denied,ec),end;it!=end;it.increment(ec)){
        const auto& p=it->path();
        if(link(p))continue;
        if(it->is_directory(ec)){
            if((descend||packageDir(p))&&p.filename().native().front()!=L'.')scan(p,files,true);
        }else if(image(p)&&it->is_regular_file(ec))files.push_back(p);
    }
}
TextureSearch::TextureSearch(const fs::path& source):model(fs::absolute(source).lexically_normal()){
    auto root=model.parent_path();
    // Loose files in user libraries can sit above unrelated projects.
    auto folderName=lower(root.filename().wstring());
    bool loose=root==root.root_path()||folderName==L"downloads"||folderName==L"desktop"||folderName==L"documents"||folderName==L"pictures";
    standalone=!loose;
    std::error_code err;
    for(fs::directory_iterator it(root,fs::directory_options::skip_permission_denied,err),end;standalone&&it!=end;it.increment(err)){
        auto ext=lower(it->path().extension().wstring());
        if((ext==L".fbx"||ext==L".obj"||ext==L".glb"||ext==L".gltf")&&lower(it->path().stem().wstring())!=lower(model.stem().wstring()))standalone=false;
    }
    scan(root,files,!loose);
    auto parent=root.parent_path();
    if(parent!=root&&parent!=parent.root_path()){
        auto outer=lower(parent.filename().wstring());
        bool packaged=(folderName==L"source"||folderName==L"models"||folderName==L"meshes"||folderName==L"geometry")&&
            outer!=L"downloads"&&outer!=L"desktop"&&outer!=L"documents";
        std::error_code ec;
        for(fs::directory_iterator it(parent,fs::directory_options::skip_permission_denied,ec),end;it!=end;it.increment(ec)){
            auto p=it->path();
            if(p!=root&&(packageDir(p)||packaged)&&p.filename().native().front()!=L'.'&&!link(p)&&it->is_directory(ec))scan(p,files,true);
        }
    }
    for(const auto& file:files)byStem[stem(file)].push_back(file);
}
bool TextureSearch::allows(const fs::path& path) const{
    auto p=(model.parent_path()/path).lexically_normal();
    if(!_wcsicmp(p.c_str(),model.c_str()))return true;
    auto root=model.parent_path();
    auto rel=p.lexically_relative(root);
    if(!rel.empty()&&*rel.begin()!=L".."){
        auto current=root;
        for(const auto& part:rel){current/=part;if(link(current))return false;}
        return image(p)||lower(p.extension().wstring())==L".bin"||lower(p.extension().wstring())==L".mtl";
    }
    return std::any_of(files.begin(),files.end(),[&](const fs::path& known){return !_wcsicmp(p.c_str(),known.c_str());});
}
fs::path TextureSearch::find(const fs::path& reference) const{
    auto filename=lower(reference.filename().wstring()),key=stem(reference);
    auto choices=byStem.find(key);
    if(key.empty()||choices==byStem.end())return {};
    int best=0;fs::path result;
    for(const auto& file:choices->second){
        int rank=lower(file.filename().wstring())==filename?100:50;
        auto a=reference.parent_path(),b=file.parent_path();
        while(!a.filename().empty()&&!b.filename().empty()&&lower(a.filename().wstring())==lower(b.filename().wstring())){
            ++rank;a=a.parent_path();b=b.parent_path();
        }
        if(rank>best){best=rank;result=file;}
        else if(rank==best)result.clear();
    }
    return result;
}

enum Role {Color,Normal,Rough,Metal,AO,Emission,Height,Unclassified};
struct MapName {Name family;Role role=Unclassified;int quality=0;unsigned version=0;};
static MapName parse(const fs::path& p){
    auto base=p.stem();if(image(base))base=base.stem();
    auto s=compact(base.wstring());MapName map;
    // A trailing v<number> identifies versions of the same exported map.
    auto v=s.find_last_not_of(L"0123456789");
    if(v!=Name::npos&&v+1<s.size()&&s[v]==L'v'&&s.size()-v<=7){
        map.version=std::stoul(s.substr(v+1));s.resize(v);
    }
    struct Ending {const wchar_t* suffix;Role role;int quality;};
    static const Ending endings[]={
        {L"basecolour",Color,3},{L"basecolor",Color,3},{L"albedo",Color,3},{L"diffuse",Color,2},{L"colour",Color,1},{L"color",Color,1},
        {L"normalopengl",Normal,3},{L"normalgl",Normal,3},{L"normalmap",Normal,2},{L"normal",Normal,2},
        {L"roughness",Rough,2},{L"metallic",Metal,2},{L"metalness",Metal,2},
        {L"ambientocclusion",AO,2},{L"mixedao",AO,1},{L"occlusion",AO,2},{L"ao",AO,1},
        {L"emissive",Emission,2},{L"emission",Emission,2},{L"height",Height,1},{L"displacement",Height,1}
    };
    for(const auto& rule:endings)if(suffix(s,rule.suffix)){
        map.family=s.substr(0,s.size()-wcslen(rule.suffix));map.role=rule.role;map.quality=rule.quality;break;
    }
    if(map.role==Unclassified){
        auto raw=lower(base.wstring());auto separator=raw.find_last_of(L"_. -");
        if(separator!=Name::npos){
            auto tag=raw.substr(separator+1);
            static const std::map<Name,Role> shortNames={{L"d",Color},{L"col",Color},{L"diff",Color},{L"n",Normal},{L"nrm",Normal},
                {L"r",Rough},{L"rough",Rough},{L"m",Metal},{L"metal",Metal},{L"e",Emission},{L"h",Height}};
            auto it=shortNames.find(tag);
            if(it!=shortNames.end()){map.family=compact(raw.substr(0,separator));map.role=it->second;map.quality=1;}
        }
    }
    return map;
}

// Creates the glTF green roughness / blue metallic texture. Defined beside the existing image codecs.
bool bindMetalRough(aiScene&,aiMaterial&,const fs::path&,const fs::path&);

bool TextureSearch::bindMaterials(aiScene& scene,bool gltf,bool preview) const{
    struct Choice {fs::path path;int quality=-1;unsigned version=0;};
    std::map<Name,std::map<Role,Choice>> sets;
    for(const auto& file:files){
        auto m=parse(file);if(m.role==Unclassified)continue;
        // A set with generic filenames uses its enclosing folder as its name.
        auto family=m.family.empty()?compact(file.parent_path().filename().wstring()):m.family;
        auto& c=sets[family][m.role];
        if(std::make_pair(m.quality,m.version)>std::make_pair(c.quality,c.version))c={file,m.quality,m.version};
        else if(m.quality==c.quality&&m.version==c.version)c.path.clear();
    }
    if(sets.empty())return false;
    std::set<unsigned> uvMaterials;
    for(unsigned i=0;i<scene.mNumMeshes;i++)if(scene.mMeshes[i]->HasTextureCoords(0))uvMaterials.insert(scene.mMeshes[i]->mMaterialIndex);
    bool changed=false;
    for(auto index:uvMaterials){
        auto& mat=*scene.mMaterials[index];aiString label;mat.Get(AI_MATKEY_NAME,label);
        Name materialName=compact(fs::u8path(label.C_Str()).wstring());
        Name family;std::set<Name> anchors,authoredFiles;fs::path colorRef;Role colorRole=Unclassified;
        bool hasMap=false,colorMissing=false;
        for(unsigned i=0;i<mat.mNumProperties;i++){
            auto* prop=mat.mProperties[i];if(prop->mKey!=aiString("$tex.file"))continue;
            auto type=static_cast<aiTextureType>(prop->mSemantic);aiString path;
            if(mat.GetTexture(type,prop->mIndex,&path)!=AI_SUCCESS)continue;
            hasMap=true;
            auto embedded=scene.GetEmbeddedTexture(path.C_Str());
            auto original=fs::u8path(embedded&&embedded->mFilename.length?embedded->mFilename.C_Str():path.C_Str());
            authoredFiles.insert(stem(original));
            auto m=parse(original);
            auto key=m.family.empty()?compact(original.parent_path().filename().wstring()):m.family;
            if(sets.count(key))anchors.insert(key);
            else if(sets.count(stem(original)))anchors.insert(stem(original));
            if((type==aiTextureType_DIFFUSE||type==aiTextureType_BASE_COLOR)&&prop->mIndex==0){
                colorRef=original;colorRole=m.role;std::error_code ec;
                colorMissing=!embedded&&!((!preview||allows(original))&&fs::is_regular_file(model.parent_path()/original,ec))&&find(original).empty();
            }
        }
        if(anchors.size()==1)family=*anchors.begin();
        else if(anchors.empty()&&sets.count(materialName))family=materialName;
        else if(anchors.empty()&&uvMaterials.size()==1&&sets.count(stem(model)))family=stem(model);
        else if(standalone&&!hasMap&&uvMaterials.size()==1&&sets.size()==1&&sets.begin()->second.count(Color))family=sets.begin()->first;
        else continue;
        const auto& maps=sets.at(family);
        auto file=[&](Role role){
            auto i=maps.find(role);
            if(i==maps.end()||authoredFiles.count(stem(i->second.path)))return fs::path{};
            return i->second.path;
        };
        auto bind=[&](aiTextureType type,const fs::path& p,bool replace=false){
            if(p.empty()||(!replace&&mat.GetTextureCount(type)))return false;
            auto utf8=p.u8string();if(utf8.size()>=sizeof(aiString().data))return false;
            aiString value(utf8);mat.AddProperty(&value,AI_MATKEY_TEXTURE(type,0));changed=true;return true;
        };
        const bool misplacedAO=colorRole==AO&&!file(Color).empty();
        if(misplacedAO){
            auto original=find(colorRef);
            if(!original.empty())bind(aiTextureType_LIGHTMAP,original);
        }
        if((colorRef.empty()||colorMissing||misplacedAO)&&bind(aiTextureType_DIFFUSE,file(Color),colorMissing||misplacedAO)){
            if(mat.GetTextureCount(aiTextureType_BASE_COLOR))bind(aiTextureType_BASE_COLOR,file(Color),true);
        }
        bind(aiTextureType_NORMALS,file(Normal));
        bind(aiTextureType_LIGHTMAP,file(AO));
        if(bind(aiTextureType_EMISSIVE,file(Emission))){
            aiColor3D white(1,1,1);mat.AddProperty(&white,1,AI_MATKEY_COLOR_EMISSIVE);
        }
        if(!gltf){
            bind(aiTextureType_DISPLACEMENT,file(Height));
            bind(aiTextureType_DIFFUSE_ROUGHNESS,file(Rough));bind(aiTextureType_METALNESS,file(Metal));
        }else if(!mat.GetTextureCount(aiTextureType_DIFFUSE_ROUGHNESS)&&!mat.GetTextureCount(aiTextureType_METALNESS)&&
                 !mat.GetTextureCount(aiTextureType_GLTF_METALLIC_ROUGHNESS)&&(!file(Rough).empty()||!file(Metal).empty())){
            if(bindMetalRough(scene,mat,file(Rough),file(Metal)))changed=true;
        }
    }
    return changed;
}
