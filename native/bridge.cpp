// C ABI for .NET. No Python interpreter or Python runtime is used.
#include "upstream/ctc_fast_decoder.h"
#include "upstream/ctc_lm_bonus.h"
#include "lm/model.hh"
#include "lm/virtual_interface.hh"
#include "sentencepiece_processor.h"
#include <string>
#include <climits>
#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif
static thread_local std::string error;
std::string utf8(int c) {
 std::string s;
 if(c<128) s+=char(c);
 else if(c<2048) { s+=char(192|(c>>6)); s+=char(128|(c&63)); }
 else { s+=char(224|(c>>12)); s+=char(128|((c>>6)&63)); s+=char(128|(c&63)); }
 return s;
}
struct KState : LMState { std::vector<uint64_t> memory; };
class KenLM : public LM {
 std::unique_ptr<lm::base::Model> model;
 std::vector<lm::WordIndex> ids;
 public:
 KenLM(const char* path,int vocab) : model(lm::ngram::LoadVirtual(path)) {
   for(int i=0;i<vocab;i++) ids.push_back(model->BaseVocabulary().Index(utf8(i+100)));
 }
 LMStatePtr start(bool empty) override {
   auto s=std::make_shared<KState>(); s->memory.resize((model->StateSize()+7)/8);
   if(empty) model->NullContextWrite(s->memory.data()); else model->BeginSentenceWrite(s->memory.data());
   return s;
 }
 std::pair<LMStatePtr,float> step(const LMStatePtr& in,int token,lm::WordIndex word) {
   auto a=std::static_pointer_cast<KState>(in); auto b=a->child<KState>(token);
   b->memory.resize((model->StateSize()+7)/8);
   float score=model->BaseScore(a->memory.data(),word,b->memory.data()); return {b,score};
 }
 std::pair<LMStatePtr,float> score(const LMStatePtr& s,int token) override { return step(s,token,ids.at(token)); }
 std::pair<LMStatePtr,float> finish(const LMStatePtr& s) override { return step(s,-1,model->BaseVocabulary().EndSentence()); }
};
class ZeroLM : public LM {
 public:
 LMStatePtr start(bool) override { return std::make_shared<LMState>(); }
 std::pair<LMStatePtr,float> score(const LMStatePtr& s,int t) override { return {s->child<LMState>(t),0}; }
 std::pair<LMStatePtr,float> finish(const LMStatePtr& s) override { return {s,0}; }
};
struct Context { sentencepiece::SentencePieceProcessor sp; LMPtr lm; std::string text; };
API const char* bb_error() { return error.c_str(); }
API void* bb_create(const char* tokenizer,const char* lm,const char* domain,double weight,double alpha,double beta) {
 try {
  auto c=std::make_unique<Context>(); auto status=c->sp.Load(tokenizer); if(!status.ok()) throw std::runtime_error(status.ToString());
  LMPtr base= alpha ? LMPtr(std::make_shared<KenLM>(lm,c->sp.GetPieceSize())) : LMPtr(std::make_shared<ZeroLM>());
  if(alpha && domain && *domain && weight>0) { auto d=std::make_shared<KenLM>(domain,c->sp.GetPieceSize()); base=weight==1 ? LMPtr(d) : std::make_shared<MixLM>(base,d,weight); }
  c->lm=std::make_shared<TokenBonusLM>(base,float(beta/(alpha?alpha:1))); return c.release();
 } catch(const std::exception& e) { error=e.what(); return nullptr; }
}
API void bb_destroy(Context* c) { delete c; }
API int bb_vocab(Context* c) { return c->sp.GetPieceSize(); }
API const char* bb_piece(Context* c,int id) { return c->sp.IdToPiece(id).c_str(); }
API const char* bb_text(Context* c,const int* tokens,int n) {
 try { std::vector<int> v(tokens,tokens+n); auto s=c->sp.Decode(v,&c->text); if(!s.ok()) throw std::runtime_error(s.ToString()); return c->text.c_str(); }
 catch(const std::exception& e) { error=e.what(); return nullptr; }
}
API int bb_decode(Context* c,const float* em,int frames,int classes,int beam,int top,double threshold,double alpha,double beta,
 const int* flat,const float* bonuses,const int* lengths,int count,int* output,int capacity) {
 try {
  if(frames<1 || classes!=c->sp.GetPieceSize()+1 || capacity<frames+2) throw std::invalid_argument("Invalid emission shape or output capacity");
  LMPtr lm=c->lm; double scale=alpha?alpha:1,upper=beta/scale;
  if(count) { std::vector<std::vector<int>> seqs; std::vector<std::vector<float>> bs; int pos=0; double mx=0;
   for(int i=0;i<count;i++) { seqs.emplace_back(flat+pos,flat+pos+lengths[i]); bs.emplace_back(bonuses+pos,bonuses+pos+lengths[i]);
    for(float b:bs.back()) mx=std::max(mx,double(b)); pos+=lengths[i]; }
   lm=std::make_shared<HotwordLM>(lm,seqs,bs); upper+=mx;
  }
  fastdec::FastCTCDecoder decoder(beam,top,threshold,scale,lm,classes-1,upper,true);
  auto path=decoder.decode(reinterpret_cast<uintptr_t>(em),frames,classes); std::copy(path.begin(),path.end(),output); return int(path.size());
 } catch(const std::exception& e) { error=e.what(); return -1; }
}
