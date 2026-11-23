#include <assimp/material.h>
#include <assimp/GltfMaterial.h>
#include <assimp/scene.h>
#include <algorithm>
#include <filesystem>
#include <fstream>
#include <memory>
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

static fs::path exportedFbxTexture(const fs::path& model,const fs::path& reference) {
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
                if(!match.empty())throw std::runtime_error("Ambiguous texture: "+reference.u8string());
                match=alternate;
            }
        }
        if(!match.empty())return match;
    }
    return {};
}

// Relink before Assimp embeds the files so the images keep their actual encoding.
void relinkFbxTextures(const aiScene& scene,const fs::path& source,bool gltf) {
    const auto model=fs::canonical(source);
    for(unsigned m=0;m<scene.mNumMaterials;m++){
        auto* material=scene.mMaterials[m];
        for(unsigned i=0;i<material->mNumProperties;i++){
            auto* prop=material->mProperties[i];
            if(prop->mKey!=aiString("$tex.file"))continue;
            auto type=static_cast<aiTextureType>(prop->mSemantic);
            aiString reference;
            if(material->GetTexture(type,prop->mIndex,&reference)!=AI_SUCCESS||reference.data[0]=='*')continue;
            aiString replacement;
            auto embedded=scene.GetEmbeddedTextureAndIndex(reference.C_Str());
            if(embedded.first)replacement.Set("*"+std::to_string(embedded.second));
            else{
                auto path=fs::u8path(reference.C_Str());
                std::error_code ec;
                if(fs::is_regular_file(path.is_absolute()?path:model.parent_path()/path,ec))continue;
                auto match=exportedFbxTexture(model,path);
                if(match.empty())continue;
                auto filename=match.u8string();
                if(filename.size()>=sizeof(replacement.data))throw std::runtime_error("Texture path too long: "+filename);
                replacement.Set(filename);
            }
            material->AddProperty(&replacement,AI_MATKEY_TEXTURE(type,prop->mIndex));
        }
        // glTF consumes opacity from base-color alpha when the FBX uses the same atlas for both.
        aiString color,opacity;
        if(gltf&&material->GetTexture(aiTextureType_DIFFUSE,0,&color)==AI_SUCCESS&&
           material->GetTexture(aiTextureType_OPACITY,0,&opacity)==AI_SUCCESS&&
           !_wcsicmp(fs::u8path(color.C_Str()).c_str(),fs::u8path(opacity.C_Str()).c_str())){
            aiString blend("BLEND");
            material->AddProperty(&blend,AI_MATKEY_GLTF_ALPHAMODE);
        }
    }
}

static void appendBytes(void* context,void* data,int size) {
    auto& bytes=*static_cast<std::vector<unsigned char>*>(context);
    auto first=static_cast<unsigned char*>(data);
    bytes.insert(bytes.end(),first,first+size);
}

static void convertToPng(aiTexture& tex) {
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
        if(!decoded)throw std::runtime_error("Cannot decode texture: "+std::string(tex.mFilename.C_Str()));
    }
    if(!stbi_write_png_to_func(appendBytes,&encoded,w,h,4,decoded?decoded.get():raw.data(),0))
        throw std::runtime_error("Could not encode a model texture.");
    auto data=std::make_unique<aiTexel[]>((encoded.size()+sizeof(aiTexel)-1)/sizeof(aiTexel));
    std::memcpy(data.get(),encoded.data(),encoded.size());
    delete[] tex.pcData;tex.pcData=data.release();tex.mWidth=static_cast<unsigned int>(encoded.size());
    tex.mHeight=0;std::strcpy(tex.achFormatHint,"png");
}

struct TextureSlot {aiMaterial* material;aiTextureType type;unsigned slot,index;};

void prepareTextures(aiScene& scene,const fs::path& output,const std::string& format) {
    const auto folder=output.stem().u8string()+".textures";
    const bool gltf=format=="glb2"||format=="gltf2";
    std::vector<TextureSlot> slots;
    // Resolve original paths before changing the embedded textures' filenames.
    for(unsigned m=0;m<scene.mNumMaterials;m++){
        auto* material=scene.mMaterials[m];
        for(unsigned i=0;i<material->mNumProperties;i++){
            auto* prop=material->mProperties[i];
            if(prop->mKey!=aiString("$tex.file"))continue;
            auto type=static_cast<aiTextureType>(prop->mSemantic);
            aiString path;material->GetTexture(type,prop->mIndex,&path);
            auto match=scene.GetEmbeddedTextureAndIndex(path.C_Str());
            if(!match.first)throw std::runtime_error("Missing texture: "+std::string(path.C_Str()));
            slots.push_back({material,type,prop->mIndex,static_cast<unsigned>(match.second)});
        }
    }
    for(unsigned i=0;i<scene.mNumTextures;i++){
        auto& tex=*scene.mTextures[i];
        std::string ext=tex.achFormatHint;
        if(ext=="jpeg")ext="jpg";
        if(tex.mHeight||ext.empty()||(gltf&&ext!="jpg"&&ext!="png"&&ext!="kx2"&&ext!="bu")){
            convertToPng(tex);ext="png";
        }
        if(ext.find_first_not_of("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")!=std::string::npos)
            throw std::runtime_error("Invalid texture format.");
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
    for(const auto& ref:slots){
        aiString path(format=="obj"?folder+"/"+scene.mTextures[ref.index]->mFilename.C_Str():"*"+std::to_string(ref.index));
        ref.material->AddProperty(&path,AI_MATKEY_TEXTURE(ref.type,ref.slot));
    }
}
