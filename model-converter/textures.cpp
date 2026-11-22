#include <assimp/material.h>
#include <assimp/scene.h>
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
