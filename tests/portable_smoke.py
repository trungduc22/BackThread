"""Run real self-contained Host/Simulator processes and exercise the local integration API. Windows only."""
import pathlib,subprocess,socket,json,time,urllib.request,urllib.error,hashlib,uuid,argparse
parser=argparse.ArgumentParser();parser.add_argument('--release',default='artifacts/release/Windows');parser.add_argument('--state-dir',default='artifacts/portable-smoke-state');args=parser.parse_args()
repo=pathlib.Path(__file__).resolve().parents[1];release=(repo/args.release).resolve();work=(repo/args.state_dir).resolve();work.mkdir(parents=True,exist_ok=True)
key=work/'session.key'
if not key.exists():subprocess.run([str(release/'BackThread.Host.exe'),'--init-key',str(key)],check=True,capture_output=True)
def port():
 with socket.socket() as s:s.bind(('127.0.0.1',0));return s.getsockname()[1]
sim_port,api_port=port(),port();assert sim_port!=api_port
config=json.loads((release/'config/example.json').read_text());config.update(keyFile=str(key),transport='localhost',devicePort=sim_port,apiPort=api_port,fixtureAudio=True)
config_path=work/'config.json';config_path.write_text(json.dumps(config));headers={'Authorization':'Bearer '+key.read_text().strip().lower(),'Content-Type':'application/json'}
logs=[];processes=[]
def spawn(name,args):
 log=open(work/(name+'-'+str(len(logs))+'.log'),'w');logs.append(log)
 p=subprocess.Popen([str(release/name),*args],cwd=work,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW);processes.append(p);return p
def request(path,body=None,method=None,authorized=True):
 data=None if body is None else json.dumps(body).encode();req=urllib.request.Request(f'http://127.0.0.1:{api_port}/v1/'+path,data=data,method=method,headers=headers if authorized else {})
 with urllib.request.urlopen(req,timeout=10) as r:
  b=r.read();return json.loads(b) if r.headers.get_content_type()=='application/json' else b
def eventually(fn,timeout=15):
 end=time.monotonic()+timeout
 while time.monotonic()<end:
  try:
   result=fn()
   if result:return result
  except (urllib.error.URLError,ConnectionError,TimeoutError):pass
  time.sleep(.1)
 raise TimeoutError('Portable smoke condition did not complete')
def start_sim():return spawn('BackThread.Simulator.exe',['--key-file',str(key),'--port',str(sim_port),'--state',str(work/'agent-state')])
report={'selfContainedWindowsExecutables':True,'physicalIPhoneTest':False}
try:
 sim=start_sim();host=spawn('BackThread.Host.exe',['--config',str(config_path)])
 def connected():
  s=request('status');return s if s.get('state')=='connected' else None
 status=eventually(connected);assert status['simulated'];report['hostConnectedToSimulator']=True
 try:request('status',authorized=False);raise AssertionError('Missing token accepted')
 except urllib.error.HTTPError as e:assert e.code==401;report['unauthorizedApiRejected']=True
 text=('BackThread portable package\n'*500).encode();source=work/'fixture.txt';source.write_bytes(text);sha=hashlib.sha256(text).hexdigest()
 uploaded=request('archives',{'path':str(source),'name':'fixture.txt'});assert uploaded['sha256']==sha;report['uploadSha256Verified']=True
 req=urllib.request.Request(f'http://127.0.0.1:{api_port}/v1/progress',headers=headers)
 with urllib.request.urlopen(req,timeout=10) as events:
  assert events.readline().startswith(b': connected');events.readline()
  jid=uuid.uuid4().hex;request('jobs',{'jobId':jid,'kind':'verify','name':'fixture.txt','expectedSha256':sha})
  assert events.readline().strip()==b'event: progress';payload=events.readline().decode();assert jid in payload;report['sseProgressReceived']=True
 state=eventually(lambda:(j if (j:=request('jobs/'+jid))['state']=='completed' else None));report['remoteIntegrityJobCompleted']=state['state']=='completed'
 assert request('artifacts/fixture.txt?offset=0&count=32768')==text;report['artifactBytesVerified']=True
 request('events',{'id':uuid.uuid4().hex,'source':'portable-smoke','title':'private','body':'sensitive fixture','priority':1,'expiresUtc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime(time.time()+60)),'sensitive':True})
 history=request('events');assert history[-1]['title']=='Private notification';report['privacyMaskVerified']=True
 sim.terminate();sim.wait(timeout=10)
 eventually(lambda:request('status')['state']!='connected');sim=start_sim();eventually(connected)
 assert request('artifacts/fixture.txt?offset=0&count=32768')==text;report['agentProcessRestartAndHostReconnect']=True;report['persistedArtifactAfterRestart']=True
 report['status']=request('status');report['passed']=True
finally:
 for p in reversed(processes):
  if p.poll() is None:p.terminate()
  try:p.wait(timeout=5)
  except subprocess.TimeoutExpired:p.kill();p.wait()
 for f in logs:f.close()
 (repo/'artifacts/portable-smoke.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
