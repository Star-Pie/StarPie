"""Prepare two isolated, user-launched cases. Never starts the product or invokes plugins."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import stat
import sys

IDS = ("starpie.builtin.launch", "starpie.builtin.folder", "starpie.builtin.weburl")
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest().upper()
def load(path): return json.loads(path.read_text(encoding="utf-8-sig"))
def get(obj, key, default=None): return next((v for k,v in obj.items() if k.lower()==key.lower()), default)
def safe_file(path):
    if path.stat().st_file_attributes & stat.FILE_ATTRIBUTE_REPARSE_POINT: raise ValueError("Reparse points are not fixture sources: " + str(path))

session=Path(sys.argv[1]).resolve()
owned=(Path(__file__).resolve().parent/"artifacts").resolve()
if owned not in session.parents or not (session/"seed-config.json").is_file(): raise ValueError("Owned generated seed required")
if (session/"session.json").exists(): raise ValueError("Session already prepared; never overwrite a prior candidate")
seed=load(session/"seed-summary.json")
candidate=Path(seed["CandidateDirectory"]).resolve()
if sha(candidate/("StarPie"+".dll"))!=seed["CandidateDllSha256"]: raise ValueError("Candidate changed")
if (candidate/"portable.flag").exists(): raise ValueError("Portable flag would escape isolated roots")
if (candidate/"plugin").exists() and any((candidate/"plugin").iterdir()): raise ValueError("Read-only discovery directory must be empty for host baseline")
source=Path(os.environ["LOCALAPPDATA"])/"StarPie"/"plugin-data"
registry_file=source/"registry.json"
registry=load(registry_file)
entries=get(registry,"entries",[])
selected=[]
for plugin_id in IDS:
    matches=[e for e in entries if get(e,"id")==plugin_id]
    if len(matches)!=1: raise ValueError("Exactly one source registration required: "+plugin_id)
    entry=matches[0]
    if get(entry,"externalPath") or get(entry,"installPath")!=plugin_id or not get(entry,"enabled"):
        raise ValueError("Only existing enabled normal packages qualify: "+plugin_id)
    folder=source/plugin_id
    safe_file(folder)
    manifest=load(folder/"plugin.json")
    assembly=get(manifest,"assembly")
    if get(manifest,"id")!=plugin_id or get(manifest,"dependencies",[]) or Path(assembly).name!=assembly or not assembly.endswith(".dll"):
        raise ValueError("Unsupported package shape: "+plugin_id)
    if sha(folder/assembly)!=get(entry,"entrySha256","").upper(): raise ValueError("Installed entry hash mismatch: "+plugin_id)
    entry=dict(entry)
    for k in list(entry):
        if k.lower()=="externalpath": entry[k]=None
        elif k.lower() in ("enabled","preload"): entry[k]=True
    selected.append((entry,folder,assembly))

cases={}
for case in ("host-only","core-three"):
    case_root=session/case
    if case_root.exists(): raise ValueError("Case directory already exists")
    env={n:str(case_root/"env"/n) for n in ("LOCALAPPDATA","APPDATA","TEMP","TMP")}
    for path in env.values(): Path(path).mkdir(parents=True)
    app=Path(env["LOCALAPPDATA"])/"StarPie"
    app.mkdir()
    shutil.copyfile(session/"seed-config.json",app/"config.json")
    plugin_root=app/"plugin-data"
    plugin_root.mkdir()
    case_entries=[]
    if case=="core-three":
        for entry,folder,assembly in selected:
            target=plugin_root/get(entry,"id")
            target.mkdir()
            for name in ("plugin.json","module.manifest.json",assembly):
                file=folder/name
                if file.exists():
                    safe_file(file)
                    shutil.copyfile(file,target/name)
                    if sha(file)!=sha(target/name): raise ValueError("Copy mismatch")
            case_entries.append(entry)
    (plugin_root/"registry.json").write_text(json.dumps({"registryVersion":1,"entries":case_entries},ensure_ascii=False,indent=2),encoding="utf-8")
    cases[case]={"Environment":env,"PluginIds":[get(e,"id") for e in case_entries],"Files":{str(p.relative_to(session)):sha(p) for p in case_root.rglob("*") if p.is_file()}}
    (session/("Start-"+case+".cmd")).write_text('@echo off\r\npowershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "'+str(Path(__file__).parent/"Start-Case.ps1")+'" -Session "'+str(session)+'" -Case '+case+'\r\nif errorlevel 1 pause\r\n',encoding="ascii")
manifest={"CandidateDirectory":str(candidate),"CandidateHashes":{n:sha(candidate/n) for n in ("StarPie.exe","StarPie"+".dll","StarPie.Plugin.Abstractions.dll")},"SeedConfigSha256":seed["ConfigSha256"],"SourceRegistryPath":str(registry_file),"SourceRegistrySha256":sha(registry_file),"Cases":cases,"ManualLaunchOnly":True}
(session/"session.json").write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding="utf-8")
print(json.dumps({"session":str(session),"cases":{k:v["PluginIds"] for k,v in cases.items()},"sameConfigSha256":seed["ConfigSha256"]},ensure_ascii=False,indent=2))
