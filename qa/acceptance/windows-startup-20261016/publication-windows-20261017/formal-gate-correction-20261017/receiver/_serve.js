"use strict";
// 同一官方应用的新Windows候选批次；保留既存文件，只允许新文件上传，不导入旧封存状态。
const crypto = require("crypto"), fs = require("fs"), http = require("http"), path = require("path");
const { pipeline } = require("stream");
const ROOT = process.env.ROOT || "/workspace", PORT = Number(process.env.PORT || 3000);
const meta = JSON.parse(fs.readFileSync(path.join(ROOT, "upload-metadata.json"), "utf8"));
const baseline = [{"file_name": "PartyOps-1.4.5-0.rc.6.1.aarch64.rpm", "bytes": 763753980, "sha256": "f507da109c1110e2c553ac9cb44a7f05205156132fb241ba057028571344e4f4", "magic_hex": "edabeedb", "content_type": "application/octet-stream"}, {"file_name": "PartyOps-1.4.5-0.rc.6.1.x86_64.rpm", "bytes": 790114068, "sha256": "e7c34a6090689f40dfae3aa3403b7b6ad0b5d4c3748e07005bb73ed1b32632ac", "magic_hex": "edabeedb", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_linux_amd64.deb", "bytes": 366480042, "sha256": "4557129b9f081c1b525d6dd6c6d444f7e0d35d63021b4b1c20c8daeffd4c30ae", "magic_hex": "213c6172", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_linux_loong64.deb", "bytes": 302195420, "sha256": "bec5993a12a9a66800fa039045f45329ecc486934cd1d1106c10d5e2ecf78017", "magic_hex": "213c6172", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_windows7_amd64.exe", "bytes": 489713934, "sha256": "996159794407f2171de170fbbf95c3b1f3fe234c026116f35113c2d8d3591d7f", "magic_hex": "4d5a5000", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_windows7_x86.exe", "bytes": 457500557, "sha256": "b3b77f2a5a319906c641ca693df31b93c3fbcb56e28eaae7a52bdfa94913bf88", "magic_hex": "4d5a5000", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_windows_amd64.exe", "bytes": 443690896, "sha256": "a7c044b0b830ce416fdf02c84daa2e7aa4929c0833d9b530bc47486d861a81be", "magic_hex": "4d5a5000", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_macos_x86_64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg", "bytes": 564985557, "sha256": "8885eea0cfa3c23f58d14109c9a46be2bf762c923cb1be58b9c549628e05c955", "magic_hex": "78617221", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_macos_arm64-UNSIGNED-UNNOTARIZED-CANDIDATE.pkg", "bytes": 588743660, "sha256": "bcc370a22545c31ad789324d1d153a0d6faa30ec61fc0632e4b004f82c0ff736", "magic_hex": "78617221", "content_type": "application/octet-stream"}, {"file_name": "PartyOps_1.4.5-rc.6_linux_arm64_kylin-full-20261016.deb", "bytes": 352447304, "sha256": "17d0e3a3acf060d3c65299c012078435bacc0da3f1f0a909fe3cc997cbbee69c", "magic_hex": "213c6172", "content_type": "application/octet-stream"}];
const candidate = {"file_name": "PartyOps_1.4.5-rc.6_windows_amd64_startup-20261017.exe", "bytes": 449978846, "sha256": "3c81fdba89781722f94e82d31c7ff99b4357a1374fc6454ad47d3cfa464d1e27", "magic_hex": "4d5a5000", "content_type": "application/octet-stream"};
const names = [...baseline.map(a=>a.file_name), candidate.file_name];
if (meta.version !== "1.4.5-rc.6" || meta.receiver !== "windows-startup-20261017-v1" ||
    meta.baseline_manifest_sha256 !== "b52807452932502a5f05a10eb609c87345fabe4addb17d3ffeae3a673e0d0e57" || meta.manifest_sha256 !== "c865bb3c179059024906d2559c7ef389a2d1e7acbaa00dce9eedf22aefabbfa3" ||
    JSON.stringify(meta.assets) !== JSON.stringify([...baseline, candidate])) throw Error("DIAGNOSTIC_METADATA_INVALID");
const tokens = JSON.parse(fs.readFileSync(path.join(ROOT, ".upload-token"), "utf8"));
if (Object.keys(tokens).length !== 1 || typeof tokens[candidate.file_name] !== "string" || !/^[a-f0-9]{64}$/.test(tokens[candidate.file_name])) throw Error("BATCH_TOKENS_INVALID");
const downloads = path.join(ROOT, "downloads"), privateRoot = path.join(ROOT, `.partyops-windows-startup-20261017-${meta.manifest_sha256}`);
fs.mkdirSync(downloads, {recursive:true}); fs.mkdirSync(privateRoot, {recursive:true, mode:0o700});
const files = new Map(meta.assets.map(a => [a.file_name, {...a, ready:false, busy:false,
  final:path.join(downloads,a.file_name), temp:path.join(privateRoot,a.file_name+".uploading"),
  seal:path.join(privateRoot,a.file_name+".sealed.json")} ]));
function json(res,status,value) { const b=Buffer.from(JSON.stringify(value));res.writeHead(status,{"Content-Type":"application/json; charset=utf-8","Content-Length":b.length,"Cache-Control":"no-store"});res.end(b); }
async function verified(file, target) {
  if (!fs.existsSync(target) || !fs.lstatSync(target).isFile() || fs.lstatSync(target).isSymbolicLink() || fs.statSync(target).size !== file.bytes) return false;
  const handle=fs.openSync(target,"r"), magic=Buffer.alloc(4); fs.readSync(handle,magic,0,4,0);fs.closeSync(handle);
  if (magic.toString("hex") !== file.magic_hex) return false;
  const hash=crypto.createHash("sha256");for await (const chunk of fs.createReadStream(target)) hash.update(chunk);
  return hash.digest("hex") === file.sha256;
}
function seal(file, reused) {
  const proof={manifest_sha256:meta.manifest_sha256,filename:file.file_name,bytes:file.bytes,sha256:file.sha256,reused};
  if (fs.existsSync(file.seal)) {
    const old=JSON.parse(fs.readFileSync(file.seal,"utf8"));
    if (old.manifest_sha256 !== proof.manifest_sha256 || old.sha256 !== proof.sha256 || old.bytes !== proof.bytes || old.filename !== proof.filename) throw Error("BATCH_SEAL_CHANGED");
  } else fs.writeFileSync(file.seal,JSON.stringify(proof),{flag:"wx",mode:0o600});
  file.ready=true;file.reused=reused;
}
function validToken(req,file) {
  const expected=Buffer.from(tokens[file.file_name]), supplied=Buffer.from(String(req.headers["x-partyops-upload-token"]||""));
  return expected.length === supplied.length && crypto.timingSafeEqual(expected,supplied);
}
function serve(req,res,file) {
  if (!file.ready) return json(res,404,{error:"not_ready"});
  let start=0,end=file.bytes-1,status=200;
  const headers={"Content-Type":file.content_type||"application/octet-stream","Accept-Ranges":"bytes","Content-Disposition":`attachment; filename="${file.file_name}"`,"X-Content-Type-Options":"nosniff"};
  if(req.headers.range) {
    const m=/^bytes=(\d+)-(\d*)$/.exec(req.headers.range);
    if(!m) return json(res,416,{error:"range_invalid"});
    start=Number(m[1]);end=Math.min(m[2]?Number(m[2]):end,end);
    if(!Number.isSafeInteger(start)||!Number.isSafeInteger(end)||start<0||start>end||start>=file.bytes) return json(res,416,{error:"range_invalid"});
    status=206;headers["Content-Range"]=`bytes ${start}-${end}/${file.bytes}`;
  }
  headers["Content-Length"]=end-start+1;res.writeHead(status,headers);
  if(req.method==="HEAD")return res.end();fs.createReadStream(file.final,{start,end}).pipe(res);
}
async function start() {
  // 每次启动实际核验既有文件；旧 marker / 旧 receiver metadata 均不能导入通过。
  // 独立十profile必须先实核原九全套，缺失/损坏不传Mac、不覆同名文件。
  for (const name of names.slice(0,10)) {
    const file=files.get(name);
    if (!await verified(file,file.final)) throw Error("TEN_BASELINE_NOT_READY");
  }
  for (const file of files.values()) {
    if (fs.existsSync(file.final)) {
      if (!await verified(file,file.final)) throw Error("EXISTING_FROZEN_FILE_MISMATCH");
      seal(file,true);
    } else if(fs.existsSync(file.seal)) throw Error("SEALED_FILE_MISSING");
  }
  http.createServer(async(req,res)=> {
    try {
      const url=new URL(req.url,"http://127.0.0.1");
      if(req.method==="GET" && url.pathname==="/health") return json(res,200,{ok:true,version:meta.version,receiver:meta.receiver,manifest_sha256:meta.manifest_sha256,
        files:Object.fromEntries([...files].map(([name,f])=>[name,{file_ready:f.ready,bytes:f.bytes,sha256:f.sha256,reused:!!f.reused}]))});
      const publicFile=[...files.values()].find(f=>url.pathname===`/downloads/${f.file_name}`);
      if(publicFile && ["GET","HEAD"].includes(req.method)) return serve(req,res,publicFile);
      if(req.method!=="POST" || !["/__partyops_upload_chunk","/__partyops_upload_finalize"].includes(url.pathname)) return json(res,404,{error:"not_found"});
      const file=files.get(String(req.headers["x-partyops-upload-filename"]||""));
      if(!file || file.file_name !== candidate.file_name || !validToken(req,file))return json(res,404,{error:"not_found"});
      if(file.file_name===names[10] && names.slice(0,10).some(name=>{
        const old=files.get(name);
        return !old.ready || !fs.existsSync(old.final) || !fs.lstatSync(old.final).isFile() ||
          fs.lstatSync(old.final).isSymbolicLink() || fs.statSync(old.final).size!==old.bytes;
      })) return json(res,409,{error:"ten_baseline_not_ready"});
      if(file.ready)return json(res,409,{error:"file_sealed"});
      if(file.busy)return json(res,409,{error:"file_busy"});
      if(url.pathname==="/__partyops_upload_chunk") {
        const length=Number(req.headers["content-length"]||0),offset=Number(req.headers["x-upload-offset"]??-1),current=fs.existsSync(file.temp)?fs.statSync(file.temp).size:0;
        if(!Number.isSafeInteger(length)||length<1||length>16*1024*1024)return json(res,413,{error:"chunk_size_invalid"});
        if(!Number.isSafeInteger(offset)||offset!==current)return json(res,409,{error:"offset_mismatch",expected_offset:current});
        if(current+length>file.bytes)return json(res,413,{error:"file_size_exceeded"});
        file.busy=true;
        pipeline(req,fs.createWriteStream(file.temp,{flags:"a",mode:0o600}),(error)=> {
          file.busy=false;if(error)return json(res,500,{error:"chunk_write_failed"});
          if(fs.statSync(file.temp).size!==current+length)return json(res,500,{error:"chunk_length_mismatch"});
          json(res,200,{ok:true,next_offset:current+length});
        });return;
      }
      file.busy=true;
      try {
        if(fs.existsSync(file.final))return json(res,409,{error:"existing_final_file"});
        if(!await verified(file,file.temp))return json(res,409,{error:"file_integrity_mismatch"});
        fs.renameSync(file.temp,file.final);seal(file,false);
        return json(res,200,{ok:true,bytes:file.bytes,sha256:file.sha256});
      } finally {file.busy=false;}
    } catch {json(res,500,{error:"internal_error"});}
  }).listen(PORT,()=>console.log("PartyOps Windows startup candidate receiver ready"));
}
start().catch(()=> {console.error("BATCH_RECEIVER_START_REJECTED");process.exitCode=1;});

