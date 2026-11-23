#pragma once
#include <assimp/scene.h>
#include <filesystem>
#include <map>
#include <string>
#include <vector>

class TextureSearch {
public:
    explicit TextureSearch(const std::filesystem::path& model);
    bool empty() const { return files.empty(); }
    bool allows(const std::filesystem::path& path) const;
    std::filesystem::path find(const std::filesystem::path& reference) const;
    bool bindMaterials(aiScene& scene,bool gltf,bool preview) const;
private:
    std::filesystem::path model;
    bool standalone=true;
    std::vector<std::filesystem::path> files;
    std::map<std::wstring,std::vector<std::filesystem::path>> byStem;
};
