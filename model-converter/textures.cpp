#include <assimp/material.h>
#include <assimp/Importer.hpp>
#include <assimp/postprocess.h>
#include <assimp/GltfMaterial.h>
#include <assimp/scene.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <memory>
#include <set>
#include <stdexcept>
#include <vector>
#define STB_IMAGE_STATIC
#define STBI_NO_STDIO
#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>
#define STB_IMAGE_WRITE_STATIC
#define STBI_WRITE_NO_STDIO
#define STB_IMAGE_WRITE_IMPLEMENTATION
#include "vendor/stb_image_write.h"

namespace fs = std::filesystem;

static bool packageImage(const fs::path& path,const fs::path& directory) {
    std::error_code ec;
    if(!fs::is_regular_file(path,ec))return false;
    auto real=fs::canonical(path,ec);
    if(ec)return false;
    auto part=real.begin();
    for(const auto& parent:directory){
        if(part==real.end()||_wcsicmp(parent.c_str(),part->c_str())!=0)return false;
        ++part;
    }
    return part!=real.end();
}

static fs::path exportedTexture(const fs::path& model,const fs::path& reference) {
    const auto root=model.parent_path();
    const auto sibling=root.parent_path()/L"textures";
    const auto name=reference.filename();
    if(name.empty()||name.native().find_first_of(L"<>:\"|?*")!=std::wstring::npos)return {};
    std::vector<fs::path> local;
    if(reference.has_parent_path()&&!reference.has_root_path()&&std::find(reference.begin(),reference.end(),L"..")==reference.end())
        local.push_back(root/reference);
    local.insert(local.end(),{root/name,root/L"textures"/name,root/(model.stem().wstring()+L".fbm")/name,sibling/name});
    const auto ext=name.extension().wstring();
    const bool editable=!_wcsicmp(ext.c_str(),L".tga")||!_wcsicmp(ext.c_str(),L".psd")||!_wcsicmp(ext.c_str(),L".psb");
    for(const auto& candidate:local){
        const auto& directory=candidate.parent_path()==sibling?sibling:root;
        if(packageImage(candidate,directory))return candidate;
        if(!editable)continue;
        fs::path match;
        for(const auto* suffix:{L".png",L".jpg",L".jpeg"}){
            for(const auto& alternate:{fs::path(candidate).replace_extension(suffix),fs::path(candidate).concat(suffix)}){
                if(!packageImage(alternate,directory))continue;
                if(!match.empty())return {};
                match=alternate;
            }
        }
        if(!match.empty())return match;
    }
    return {};
}

struct TextureSlot {aiMaterial* material;aiTextureType type;unsigned slot;int index;std::string name;};

// Retain authored filenames for the completion dialog before embedding replaces them with indices.
static std::vector<TextureSlot> relinkTextures(const aiScene& scene,const fs::path& source,bool fbxToGltf) {
    std::vector<TextureSlot> slots;
    const auto model=fs::canonical(source);
    for(unsigned m=0;m<scene.mNumMaterials;m++){
        auto* material=scene.mMaterials[m];
        for(unsigned i=0;i<material->mNumProperties;i++){
            auto* prop=material->mProperties[i];
            if(prop->mKey!=aiString("$tex.file"))continue;
            auto type=static_cast<aiTextureType>(prop->mSemantic);
            aiString reference;
            if(material->GetTexture(type,prop->mIndex,&reference)!=AI_SUCCESS)continue;
            auto embedded=scene.GetEmbeddedTextureAndIndex(reference.C_Str());
            const char* name=reference.C_Str();
            if(reference.data[0]=='*'&&embedded.first&&embedded.first->mFilename.length)name=embedded.first->mFilename.C_Str();
            slots.push_back({material,type,prop->mIndex,-1,name});
            if(reference.data[0]=='*')continue;
            aiString replacement;
            if(embedded.first)replacement.Set("*"+std::to_string(embedded.second));
            else{
                auto path=fs::u8path(reference.C_Str());
                std::error_code ec;
                if(fs::is_regular_file(path.is_absolute()?path:model.parent_path()/path,ec))continue;
                auto match=exportedTexture(model,path);
                if(match.empty())continue;
                auto filename=match.u8string();
                if(filename.size()>=sizeof(replacement.data))continue;
                replacement.Set(filename);
            }
            material->AddProperty(&replacement,AI_MATKEY_TEXTURE(type,prop->mIndex));
        }
        // glTF consumes opacity from base-color alpha when the FBX uses the same atlas for both.
        aiString color,opacity;
        if(fbxToGltf&&material->GetTexture(aiTextureType_DIFFUSE,0,&color)==AI_SUCCESS&&
           material->GetTexture(aiTextureType_OPACITY,0,&opacity)==AI_SUCCESS&&
           !_wcsicmp(fs::u8path(color.C_Str()).c_str(),fs::u8path(opacity.C_Str()).c_str())){
            std::error_code ec;
            if(scene.GetEmbeddedTexture(color.C_Str())||fs::is_regular_file(model.parent_path()/fs::u8path(color.C_Str()),ec)){
                aiString blend("BLEND");
                material->AddProperty(&blend,AI_MATKEY_GLTF_ALPHAMODE);
            }
        }
    }
    return slots;
}

static void appendBytes(void* context,void* data,int size) {
    auto& bytes=*static_cast<std::vector<unsigned char>*>(context);
    auto first=static_cast<unsigned char*>(data);
    bytes.insert(bytes.end(),first,first+size);
}

static bool convertToPng(aiTexture& tex) {
    int w=0,h=0,channels=0;
    std::vector<unsigned char> raw,encoded;
    std::unique_ptr<unsigned char,decltype(&stbi_image_free)> decoded(nullptr,stbi_image_free);
    if(tex.mHeight){
        w=tex.mWidth;h=tex.mHeight;raw.reserve(size_t(w)*h*4);
        for(size_t i=0;i<size_t(w)*h;i++){
            const auto& p=tex.pcData[i];raw.insert(raw.end(),{p.r,p.g,p.b,p.a});
        }
    }else{
        decoded.reset(stbi_load_from_memory(reinterpret_cast<unsigned char*>(tex.pcData),
                                            tex.mWidth,&w,&h,&channels,4));
        if(!decoded)return false;
    }
    if(!stbi_write_png_to_func(appendBytes,&encoded,w,h,4,decoded?decoded.get():raw.data(),0))
        throw std::runtime_error("Could not encode a model texture.");
    auto data=std::make_unique<aiTexel[]>((encoded.size()+sizeof(aiTexel)-1)/sizeof(aiTexel));
    std::memcpy(data.get(),encoded.data(),encoded.size());
    delete[] tex.pcData;tex.pcData=data.release();tex.mWidth=static_cast<unsigned int>(encoded.size());
    tex.mHeight=0;std::strcpy(tex.achFormatHint,"png");
    return true;
}

// The Assimp exporters enumerate each texture stack starting at zero. Keep UV
// channels and sampler properties with their surviving layers when a map is absent.
static void removeSlot(aiMaterial& material,aiTextureType type,unsigned slot) {
    for(unsigned i=material.mNumProperties;i-->0;){
        auto* prop=material.mProperties[i];
        if(prop->mSemantic==type&&prop->mIndex==slot&&
           (type!=aiTextureType_NONE||std::strncmp(prop->mKey.C_Str(),"$tex.",5)==0))
            material.RemoveProperty(prop->mKey.C_Str(),type,slot);
    }
    for(unsigned i=0;i<material.mNumProperties;i++){
        auto* prop=material.mProperties[i];
        if(prop->mSemantic==type&&prop->mIndex>slot&&
           (type!=aiTextureType_NONE||std::strncmp(prop->mKey.C_Str(),"$tex.",5)==0))--prop->mIndex;
    }
}

std::unique_ptr<aiScene> prepareTextures(Assimp::Importer& reader,const fs::path& source,const fs::path& output,const std::string& format,std::set<std::string>& skipped) {
    const auto folder=output.stem().u8string()+".textures";
    const bool gltf=format=="glb2"||format=="gltf2";
    auto slots=relinkTextures(*reader.GetScene(),source,gltf&&!_wcsicmp(source.extension().c_str(),L".fbx"));
    if(!reader.ApplyPostProcessing(aiProcess_EmbedTextures))throw std::runtime_error(reader.GetErrorString());
    std::unique_ptr<aiScene> owned(reader.GetOrphanedScene());
    auto& scene=*owned;
    for(auto& ref:slots){
        aiString path;
        if(ref.material->GetTexture(ref.type,ref.slot,&path)==AI_SUCCESS)
            ref.index=scene.GetEmbeddedTextureAndIndex(path.C_Str()).second;
    }
    std::vector<int> indices(scene.mNumTextures,-1);
    unsigned kept=0;
    for(unsigned i=0;i<scene.mNumTextures;i++){
        auto& tex=*scene.mTextures[i];
        std::string ext=tex.achFormatHint;
        if(ext=="jpeg")ext="jpg";
        if(!tex.mWidth||!tex.pcData)continue;
        if(tex.mHeight||ext.empty()||(gltf&&ext!="jpg"&&ext!="png"&&ext!="kx2"&&ext!="bu")){
            if(!convertToPng(tex))continue;
            ext="png";
        }
        if(ext.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")!=std::string::npos)continue;
        indices[i]=kept++;
        const auto filename="texture-"+std::to_string(i+1)+"."+(ext=="kx2"?"ktx2":ext);
        tex.mFilename.Set(filename);
        if(format=="obj"){
            const auto dir=output.parent_path()/fs::u8path(folder);
            fs::create_directories(dir);
            std::ofstream file(dir/fs::u8path(filename),std::ios::binary);
            file.exceptions(std::ios::badbit|std::ios::failbit);
            file.write(reinterpret_cast<const char*>(tex.pcData),tex.mWidth);file.close();
        }
    }
    // Process higher layers first so removing a layer cannot invalidate pending slot numbers.
    std::sort(slots.begin(),slots.end(),[](const TextureSlot& a,const TextureSlot& b){return a.slot>b.slot;});
    for(const auto& ref:slots){
        if(ref.index<0||indices[ref.index]<0){
            skipped.insert(ref.name);
            removeSlot(*ref.material,ref.type,ref.slot);
            continue;
        }
        aiString path(format=="obj"?folder+"/"+scene.mTextures[ref.index]->mFilename.C_Str():"*"+std::to_string(indices[ref.index]));
        ref.material->AddProperty(&path,AI_MATKEY_TEXTURE(ref.type,ref.slot));
    }
    for(unsigned i=0;i<scene.mNumTextures;i++){
        if(indices[i]<0)delete scene.mTextures[i];
        else scene.mTextures[indices[i]]=scene.mTextures[i];
    }
    scene.mNumTextures=kept;
    return owned;
}
