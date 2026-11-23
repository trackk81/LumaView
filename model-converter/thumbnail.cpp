#include <assimp/DefaultIOSystem.h>
#include <assimp/Importer.hpp>
#include <assimp/material.h>
#include <assimp/postprocess.h>
#include <assimp/scene.h>
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cwctype>
#include <filesystem>
#include <fstream>
#include <limits>
#include <stdexcept>
#include <string>
#include <vector>
#include <fcntl.h>
#include <io.h>

namespace fs=std::filesystem;
using Bytes=std::vector<unsigned char>;
constexpr uint64_t maxInput=512ULL*1024*1024;

static void readExact(void* out,size_t bytes){
    if(std::fread(out,1,bytes,stdin)!=bytes)throw std::runtime_error("Incomplete model stream.");
}
static void writeExact(const void* data,size_t bytes){
    if(std::fwrite(data,1,bytes,stdout)!=bytes)throw std::runtime_error("Thumbnail pipe closed.");
}

// ReadFileFromMemory supplies the main model. Only local glTF buffers / OBJ materials
// below its directory may be opened in addition to the supplied bytes.
class CompanionFiles final : public Assimp::IOSystem {
    Assimp::DefaultIOSystem disk;
    fs::path folder,extension;
    fs::path resolve(const char* file) const {
        if(folder.empty())return {};
        fs::path requested=fs::u8path(file);
        if(requested.is_absolute()||requested.has_root_name()||requested.has_root_directory())return {};
        for(const auto& part:requested)
            if(part==L".."||part.native().find(L':')!=std::wstring::npos)return {};
        auto ext=requested.extension().wstring();
        if(extension==L".gltf" ? _wcsicmp(ext.c_str(),L".bin")!=0 :
           extension!=L".obj"||_wcsicmp(ext.c_str(),L".mtl")!=0)return {};
        std::error_code ec;
        auto candidate=fs::canonical(folder/requested,ec);
        if(ec)return {};
        auto relative=candidate.lexically_relative(folder);
        if(relative.empty()||relative.is_absolute()||*relative.begin()==L"..")return {};
        if(!fs::is_regular_file(candidate,ec)||fs::file_size(candidate,ec)>maxInput||ec)return {};
        return candidate;
    }
public:
    CompanionFiles(const fs::path& origin,const std::wstring& ext):extension(ext){
        if(origin.is_absolute())folder=fs::canonical(origin.parent_path());
    }
    bool Exists(const char* file) const override{return !resolve(file).empty();}
    char getOsSeparator() const override{return '/';}
    void Close(Assimp::IOStream* stream) override{disk.Close(stream);}
    Assimp::IOStream* Open(const char* file,const char* mode="rb") override{
        if(std::string(mode)!="rb"&&std::string(mode)!="r")return nullptr;
        auto path=resolve(file);
        return path.empty()?nullptr:disk.Open(path.u8string().c_str(),"rb");
    }
};

struct Point {double x,y,z;};
static Point project(const aiVector3D& v){
    return {.8*v.x-.6*v.z, .216*v.x-.93295*v.y+.288*v.z, .55977*v.x+.36*v.y+.74636*v.z};
}
static bool finite(Point p){return std::isfinite(p.x)&&std::isfinite(p.y)&&std::isfinite(p.z);}
static double edge(const Point& a,const Point& b,double x,double y){return (b.x-a.x)*(y-a.y)-(b.y-a.y)*(x-a.x);}
struct Tile {
    int side;
    Bytes pixels;
    std::vector<float> depth;
    explicit Tile(int n):side(n),pixels(n*n*4,0),depth(n*n,-std::numeric_limits<float>::infinity()){}
    void triangle(Point a,Point b,Point c,const aiColor4D& color){
        double area=edge(a,b,c.x,c.y);
        if(std::abs(area)<1e-8)return;
        auto ab=Point{b.x-a.x,b.y-a.y,b.z-a.z},ac=Point{c.x-a.x,c.y-a.y,c.z-a.z};
        double nx=ab.y*ac.z-ab.z*ac.y,ny=ab.z*ac.x-ab.x*ac.z,nz=area;
        double norm=std::sqrt(nx*nx+ny*ny+nz*nz);
        double lit=(-.32*nx-.58*ny+.75*nz)*(nz<0?-1:1)/norm;
        double light=.49+.51*std::max(0.,lit);
        auto channel=[light](float v){return static_cast<unsigned char>((38+208*std::clamp(std::isfinite(v)?double(v):.65,0.,1.))*light);};
        unsigned char rgb[]={channel(color.b),channel(color.g),channel(color.r),255};
        int left=std::max(0,int(std::floor(std::min({a.x,b.x,c.x}))));
        int top=std::max(0,int(std::floor(std::min({a.y,b.y,c.y}))));
        int right=std::min(side-1,int(std::ceil(std::max({a.x,b.x,c.x}))));
        int bottom=std::min(side-1,int(std::ceil(std::max({a.y,b.y,c.y}))));
        for(int y=top;y<=bottom;++y)for(int x=left;x<=right;++x){
            double u=edge(b,c,x+.5,y+.5)/area,v=edge(c,a,x+.5,y+.5)/area,w=1-u-v;
            if(u<0||v<0||w<0)continue;
            float z=static_cast<float>(u*a.z+v*b.z+w*c.z);
            size_t i=y*side+x;
            if(z<depth[i])continue;
            depth[i]=z;std::copy_n(rgb,4,pixels.data()+i*4);
        }
    }
};

static Tile render(const Bytes& data,const fs::path& origin,int side){
    auto ext=origin.extension().wstring();
    std::transform(ext.begin(),ext.end(),ext.begin(),[](wchar_t c){return wchar_t(towlower(c));});
    Assimp::Importer importer;
    importer.SetIOHandler(new CompanionFiles(origin,ext));
    auto hint=fs::path(ext).u8string();
    const aiScene* scene=importer.ReadFileFromMemory(data.data(),data.size(),
        aiProcess_Triangulate|aiProcess_PreTransformVertices|aiProcess_SortByPType,hint.empty()?"":hint.c_str()+1);
    if(!scene||!scene->HasMeshes())throw std::runtime_error("No model geometry.");
    Point low={INFINITY,INFINITY,INFINITY},high={-INFINITY,-INFINITY,-INFINITY};
    for(unsigned i=0;i<scene->mNumMeshes;++i){
        const aiMesh& mesh=*scene->mMeshes[i];
        if(!(mesh.mPrimitiveTypes&aiPrimitiveType_TRIANGLE))continue;
        for(unsigned j=0;j<mesh.mNumVertices;++j){
            Point p=project(mesh.mVertices[j]);if(!finite(p))continue;
            low={std::min(low.x,p.x),std::min(low.y,p.y),std::min(low.z,p.z)};
            high={std::max(high.x,p.x),std::max(high.y,p.y),std::max(high.z,p.z)};
        }
    }
    double span=std::max(high.x-low.x,high.y-low.y);
    if(!std::isfinite(span)||span<=0)throw std::runtime_error("Empty bounds.");
    double scale=side*.88/span;
    Tile tile(side);
    for(unsigned i=0;i<scene->mNumMeshes;++i){
        const auto& mesh=*scene->mMeshes[i];
        if(!(mesh.mPrimitiveTypes&aiPrimitiveType_TRIANGLE))continue;
        aiColor4D base(.67f,.74f,.83f,1);
        if(mesh.mMaterialIndex<scene->mNumMaterials){
            auto mat=scene->mMaterials[mesh.mMaterialIndex];
            if(mat->Get(AI_MATKEY_BASE_COLOR,base)!=AI_SUCCESS)mat->Get(AI_MATKEY_COLOR_DIFFUSE,base);
        }
        std::vector<Point> vertices(mesh.mNumVertices);
        for(unsigned j=0;j<mesh.mNumVertices;++j){
            auto p=project(mesh.mVertices[j]);
            vertices[j]={(p.x-(low.x+high.x)*.5)*scale+side*.5,(p.y-(low.y+high.y)*.5)*scale+side*.5,(p.z-low.z)*scale};
        }
        for(unsigned j=0;j<mesh.mNumFaces;++j){
            const auto& f=mesh.mFaces[j];if(f.mNumIndices!=3)continue;
            const auto* k=f.mIndices;
            if(k[0]>=vertices.size()||k[1]>=vertices.size()||k[2]>=vertices.size())continue;
            auto a=vertices[k[0]],b=vertices[k[1]],c=vertices[k[2]];
            if(!finite(a)||!finite(b)||!finite(c))continue;
            aiColor4D color=base;
            if(mesh.HasVertexColors(0))color=(mesh.mColors[0][k[0]]+mesh.mColors[0][k[1]]+mesh.mColors[0][k[2]])*(1.f/3);
            tile.triangle(a,b,c,color);
        }
    }
    if(std::none_of(tile.depth.begin(),tile.depth.end(),[](float z){return std::isfinite(z);}))
        throw std::runtime_error("No visible triangles.");
    return tile;
}

int thumbnail(int argc,wchar_t** argv){
    const std::wstring mode=argv[1];
    int side=std::stoi(argv[2]);if(side<1||side>1024)return 2;
    _setmode(_fileno(stdout),_O_BINARY);_setmode(_fileno(stdin),_O_BINARY);
    Bytes data;fs::path origin;
    if(mode==L"--thumbnail-file"&&argc==4){
        origin=fs::absolute(argv[3]);auto size=fs::file_size(origin);
        if(size==0||size>maxInput)return 1;
        data.resize(static_cast<size_t>(size));std::ifstream file(origin,std::ios::binary);
        if(!file.read(reinterpret_cast<char*>(data.data()),data.size()))return 1;
    }else if(mode==L"--thumbnail-stream"&&argc==3){
        uint64_t size;uint32_t chars;
        readExact(&size,8);readExact(&chars,4);
        if(!size||size>maxInput||chars>32767)return 1;
        std::wstring name(chars,0);readExact(name.data(),chars*sizeof(wchar_t));origin=name;
        data.resize(static_cast<size_t>(size));
        for(uint64_t offset=0;offset<size;){
            unsigned char op='R';uint32_t want=static_cast<uint32_t>(std::min<uint64_t>(128*1024,size-offset)),got;
            writeExact(&op,1);writeExact(&offset,8);writeExact(&want,4);std::fflush(stdout);
            readExact(&got,4);if(!got||got>want)return 1;readExact(data.data()+offset,got);offset+=got;
        }
    }else return 2;
    Tile tile=render(data,origin,side);
    const char op='B';writeExact(&op,1);writeExact(&side,4);writeExact(&side,4);
    writeExact(tile.pixels.data(),tile.pixels.size());return 0;
}
