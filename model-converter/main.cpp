#include <assimp/Importer.hpp>
#include <assimp/Exporter.hpp>
#include <assimp/scene.h>
#include <assimp/postprocess.h>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <memory>
#include <set>
#include <stdexcept>
#include <string>

namespace fs = std::filesystem;

std::unique_ptr<aiScene> prepareTextures(Assimp::Importer&,const fs::path&,const fs::path&,const std::string&,std::set<std::string>&);
int thumbnail(int argc,wchar_t** argv);

static void convert(const fs::path& source,const fs::path& output) {
    const auto ext=output.extension().string();
    std::string format;
    if(ext==".fbx")format="fbx";
    else if(ext==".glb")format="glb2";
    else if(ext==".gltf")format="gltf2";
    else if(ext==".obj")format="obj";
    else if(ext==".stl")format="stlb";
    else throw std::runtime_error("Unsupported output format.");
    if(fs::exists(output))throw std::runtime_error("Destination exists.");
    Assimp::Importer reader;
    const auto imported=reader.ReadFile(source.u8string(),aiProcess_Triangulate);
    if(!imported)
        throw std::runtime_error(reader.GetErrorString());
    const bool fbx=_wcsicmp(source.extension().c_str(),L".fbx")==0;
    if(!imported->HasMeshes()||!imported->mRootNode)throw std::runtime_error("The source contains no meshes.");
    std::set<std::string> skipped;
    auto scene=format=="stlb"?std::unique_ptr<aiScene>(reader.GetOrphanedScene()):prepareTextures(reader,source,output,format,skipped);
    if(fbx){
        // The FBX importer puts the original basis and cm/unit on the root.
        aiMatrix4x4 meters;aiMatrix4x4::Scaling(aiVector3D(.01f),meters);
        scene->mRootNode->mTransformation=meters*scene->mRootNode->mTransformation;
    }
    if(format=="fbx"){
        // The FBX writer treats the scene root as an implicit document node.
        // Keep the imported root as a real node so its meshes and animation survive.
        auto parent=std::make_unique<aiNode>();
        aiNode* child=scene->mRootNode;parent->addChildren(1,&child);scene->mRootNode=parent.release();
        if(!scene->mMetaData)scene->mMetaData=new aiMetadata();
        scene->mMetaData->Add("UnitScaleFactor",100.0);
        scene->mMetaData->Add("OriginalUnitScaleFactor",100.0);
    }
    Assimp::Exporter writer;
    if(writer.Export(scene.get(),format,output.u8string())!=AI_SUCCESS)
        throw std::runtime_error(writer.GetErrorString());
    if(!fs::is_regular_file(output)||!fs::file_size(output))throw std::runtime_error("Empty conversion output.");
    for(const auto& name:skipped)std::cerr<<"Skipped texture: "<<name<<'\n';
}

int wmain(int argc,wchar_t** argv) {
    try{
        if(argc>=3&&(std::wstring(argv[1])==L"--thumbnail-stream"||std::wstring(argv[1])==L"--thumbnail-file"))return thumbnail(argc,argv);
        if(argc!=3)return 2;
        convert(fs::path(argv[1]),fs::path(argv[2]));return 0;
    }
    catch(const std::exception& e){std::cerr<<e.what()<<'\n';return 1;}
}
