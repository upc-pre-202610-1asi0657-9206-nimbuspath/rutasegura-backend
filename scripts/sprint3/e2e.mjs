import assert from 'node:assert/strict'
import {randomUUID} from 'node:crypto'
import {mkdir,writeFile} from 'node:fs/promises'
import {join} from 'node:path'
import {pathToFileURL} from 'node:url'
import {spawnSync} from 'node:child_process'
const project=process.env.SPRINT3_ISOLATED_PROJECT
assert.match(project||'',/^sprint3-verify-[0-9a-f]{12}$/,'Only the generated isolated compose project is accepted.')
const gateway=process.env.SPRINT3_GATEWAY_URL, monolith=process.env.SPRINT3_MONOLITH_URL
for(const base of [gateway,monolith])assert.ok(['127.0.0.1','localhost'].includes(new URL(base).hostname),'Only loopback isolated APIs are accepted.')
const out=process.env.SPRINT3_ARTIFACTS
await mkdir(out,{recursive:true})
const report={project,startedAtUtc:new Date().toISOString(),checks:[],boardingToInboxMilliseconds:[],webVerified:false,mobileDeviceVerified:false,firebaseRealVerified:false}
const sleep=ms=>new Promise(resolve=>setTimeout(resolve,ms))
function check(label){report.checks.push(label);console.log('OK: '+label)}
async function request(path,{method='GET',token,body,expected=200,base=gateway,headers={}}={}){
 const result=await fetch(base+path,{method,headers:{...(token?{Authorization:'Bearer '+token}:{}),...(body!==undefined?{'Content-Type':'application/json'}:{}),...headers},body:body===undefined?undefined:JSON.stringify(body),signal:AbortSignal.timeout(15000)})
 if(result.status!==expected){const problem=await result.json().catch(()=>({}));throw new Error(`${method} ${path.split('?')[0]} returned ${result.status}, expected ${expected}; code=${problem.code || problem.title || 'unknown'}`)}
 if(result.status===204)return null
 const type=result.headers.get('content-type')||''
 return type.includes('json')?result.json():result.text()
}
async function until(label,fn,timeout=60000){const deadline=Date.now()+timeout;while(Date.now()<deadline){const value=await fn();if(value){check(label);return value}await sleep(500)}throw new Error('Timeout: '+label)}
function compose(...args){const r=spawnSync('docker',['compose',...JSON.parse(process.env.SPRINT3_COMPOSE_ARGUMENTS),...args],{encoding:'utf8',timeout:90000});if(r.status!==0)throw new Error('Isolated compose operation failed: '+args[0]);}
async function createOrganization(index){
 const credentials={email:`admin-${index}-${project}@integration.test`,password:randomUUID()+'Aa1!'}
 const result=await request('/api/v1/organizations',{base:monolith,method:'POST',expected:201,headers:{'X-Provisioning-Key':'local-docker-provisioning-key-0123456789abcdef'},body:{name:`Operación ${index} ${project}`,administrator:{firstName:'Admin',lastName:'Prueba',...credentials}}})
 const session=await request('/api/v1/users/sign-in',{method:'POST',body:credentials})
 return {organizationId:result.organization.id,token:session.token,credentials}
}
async function account(admin,role){
 const credentials={email:`${role}-${project}@integration.test`,password:randomUUID()+'Aa1!'}
 const person=await request('/api/v1/users/'+(role==='driver'?'drivers':'parents'),{method:'POST',token:admin.token,expected:201,body:{firstName:role==='driver'?'Conductor':'Tutor',lastName:'Prueba',...credentials}})
 const login=await request('/api/v1/users/sign-in',{method:'POST',body:credentials})
 return {id:person.id,token:login.token,credentials}
}
async function socket(token,tripId){
 const n=await request('/hubs/trip-tracking/negotiate?negotiateVersion=1',{method:'POST',token})
 const url=new URL('/hubs/trip-tracking',gateway);url.protocol='ws:';url.searchParams.set('id',n.connectionToken);url.searchParams.set('access_token',token)
 const ws=new WebSocket(url),messages=[],pending=new Map();let sequence=0,handshake
 const ready=new Promise((resolve,reject)=>{handshake=resolve;ws.addEventListener('error',()=>reject(new Error('SignalR transport failed')),{once:true})})
 ws.addEventListener('open',()=>ws.send(JSON.stringify({protocol:'json',version:1})+'\u001e'))
 ws.addEventListener('message',event=>{
  for(const frame of String(event.data).split('\u001e').filter(Boolean)){
   const message=JSON.parse(frame)
   if(!message.type){handshake();continue}
   if(message.type===1)messages.push(message)
   if(message.type===3){const call=pending.get(message.invocationId);pending.delete(message.invocationId);message.error?call?.reject(new Error('SignalR invocation rejected')):call?.resolve(message.result)}
  }
 })
 async function invoke(target,...args){const invocationId=String(++sequence);const result=new Promise((resolve,reject)=>pending.set(invocationId,{resolve,reject}));ws.send(JSON.stringify({type:1,target,arguments:args,invocationId})+'\u001e');return Promise.race([result,sleep(10000).then(()=>{throw new Error('SignalR invocation timeout')})])}
 await Promise.race([ready,sleep(10000).then(()=>{throw new Error('SignalR handshake timeout')})]);await invoke('SubscribeToTrip',tripId)
 return {ws,messages,get closed(){return ws.readyState===WebSocket.CLOSED},close:()=>ws.close()}
}
let browser,contextParent,contextAdmin,sockets=[]
try{
 await request('/health/ready');check('Gateway real readiness')
 const admin=await createOrganization(1),other=await createOrganization(2)
 const driver=await account(admin,'driver'),parent=await account(admin,'parent')
 await request('/api/v1/stakeholders/me/profile',{method:'PUT',token:parent.token,body:{phoneNumber:'999888777',documentNumber:'12345678'}})
 await request('/api/v1/stakeholders/administration/guardians',{method:'POST',token:admin.token,body:{guardianUserId:parent.id,phoneNumber:'999888777',documentNumber:'12345678'}})
 const child=await request('/api/v1/stakeholders/administration/students',{method:'POST',token:admin.token,expected:201,body:{guardianUserId:parent.id,student:{firstName:'Ana',lastName:'Integración',birthDate:'2017-02-01',schoolName:'Colegio de prueba',schoolGrade:'Primaria',pickupLocation:{address:'Recojo de prueba Lima',latitude:-12.111,longitude:-77.021}}}})
 const childId=child.studentId
 const roster=await request('/api/v1/roster/drivers',{method:'POST',token:admin.token,expected:201,body:{userId:driver.id,firstName:'Conductor',lastName:'Prueba',documentNumber:'87654321',licenseNumber:'Q12345678',licenseCategory:'A-IIB',licenseExpiresOn:'2030-12-31',phoneNumber:'999777888'}})
 await request(`/api/v1/roster/drivers/${roster.id}/verify`,{method:'POST',token:admin.token})
 const vehicle=await request('/api/v1/roster/vehicles',{method:'POST',token:admin.token,expected:201,body:{licensePlate:'TST-123',seatingCapacity:10,brand:'Prueba',model:'Integración'}})
 const serviceDate=new Intl.DateTimeFormat('en-CA',{timeZone:'America/Lima',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date())
 // Nonoverlapping windows in the same day; the server accepts a late start and marks it Delayed.
 const routes=[]
 for(const [direction,departureTime,estimatedArrivalTime] of [['PICKUP','00:01:00','00:02:00'],['RETURN','00:04:00','00:05:00']]){
  let route=await request('/api/v1/routes',{method:'POST',token:admin.token,expected:201,body:{name:`Ruta ${direction} ${project}`,direction,departureTime,estimatedArrivalTime,timeZoneId:'America/Lima',operatingDays:['Monday','Tuesday','Wednesday','Thursday','Friday','Saturday','Sunday']}})
  await request(`/api/v1/routes/${route.id}/stops`,{method:'POST',token:admin.token,body:{name:'Colegio',address:'Colegio de prueba',latitude:-12.113,longitude:-77.023}})
  await request(`/api/v1/routes/${route.id}/assignment`,{method:'PUT',token:admin.token,body:{driverId:roster.id,vehicleId:vehicle.id}})
  route=await request(`/api/v1/routes/${route.id}/activate`,{method:'POST',token:admin.token})
  await request(`/api/v1/stakeholders/me/students/${childId}/enrollments`,{method:'POST',token:parent.token,expected:201,body:{enrollmentCode:route.enrollmentCode}})
  const scheduled=await until('Ruta disponible para programación '+direction,async()=>{try{return await request('/api/v1/trips',{method:'POST',token:admin.token,expected:201,body:{routeId:route.id,serviceDate}})}catch(e){if(e.message.includes('returned 404'))return false;throw e}})
  const agenda=await until('Publicación de ruta y programación real '+direction,async()=>{
   const rows=await request('/api/v1/drivers/me/trips',{token:driver.token});return rows.find(t=>t.routeId===route.id||t.routeName===route.name)
  })
  const tripId=agenda.tripId||scheduled.tripId
  await until('Alumno propagado al viaje '+direction,async()=>{const live=await request(`/api/v1/trips/${tripId}/live`,{token:driver.token});return live.passengers.some(p=>p.studentId===childId)})
  routes.push({id:route.id,tripId,direction})
 }
 const link=`/api/v1/stakeholders/administration/guardian-links/${parent.id}/${childId}`
 await request(`/api/v1/trips/${routes[0].tripId}/live`,{token:parent.token,expected:403});check('Inscripción no concede tutela automáticamente')
 await request(link+'/verify',{method:'POST',token:admin.token,body:{reference:'Revisión de prueba aislada'}})
 await request(`/api/v1/trips/${routes[0].tripId}/live`,{token:other.token,expected:403});check('Aislamiento entre transportistas')
 const contexts=await request('/api/v1/notifications/contexts',{token:parent.token});assert.ok(contexts.organizationIds.includes(admin.organizationId));check('Contexto PARENT sin tenant firmado')
 if(process.env.SPRINT3_WEB_DIRECTORY){
  const {chromium}=await import(pathToFileURL(join(process.env.SPRINT3_WEB_DIRECTORY,'node_modules/@playwright/test/index.mjs')).href)
  browser=await chromium.launch(process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE?{executablePath:process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE}:{})
  // Vite is started by the orchestrator on an isolated port and points to this Gateway.
  const web=process.env.SPRINT3_WEB_URL
  const login=async(role,credentials)=>{const context=await browser.newContext();const page=await context.newPage();await page.goto(web+'/login');await page.getByLabel('Correo electrónico',{exact:true}).fill(credentials.email);await page.getByLabel('Contraseña',{exact:true}).fill(credentials.password);await page.getByRole('button',{name:'Ingresar a RutaSegura'}).click();await page.waitForURL(role==='PARENT'?'**/parent':'**/admin/routes');return{context,page}}
  contextParent=await login('PARENT',parent.credentials);contextAdmin=await login('ADMIN',admin.credentials)
 }
 for(const route of routes){
  const tripId=route.tripId
  await request(`/api/v1/trips/${tripId}/start`,{method:'POST',token:driver.token,body:{clientEventId:randomUUID()}})
  // Force delayed via an incident as well, to avoid depending on the current time of day.
  await request(`/api/v1/trips/${tripId}/incidents`,{method:'POST',token:driver.token,body:{clientEventId:randomUUID(),incidentType:'HeavyTraffic',detail:'Prueba aislada de retraso',notifyGuardians:true}})
  const live=await request(`/api/v1/trips/${tripId}/live`,{token:parent.token});assert.equal(live.status,'Delayed')
  const sub=await socket(parent.token,tripId);sockets.push(sub)
  const reading={latitude:-12.111,longitude:-77.021,recordedAt:new Date().toISOString(),accuracyMeters:5,speedKmh:20}
  const gps=await request('/api/v1/tracking/positions',{method:'POST',token:driver.token,body:{tripId,readings:[reading]}});assert.equal(gps.accepted,1)
  await until('GPS SignalR real en Delayed '+route.direction,async()=>sub.messages.some(m=>m.target==='PositionUpdated'&&m.arguments[0].status==='Delayed'),15000)
  if(contextParent){
   const page=contextParent.page;await page.goto(process.env.SPRINT3_WEB_URL+'/parent/tracking');await page.getByLabel('Viaje autorizado',{exact:true}).selectOption(tripId);await page.getByRole('region',{name:'Ubicación del vehículo y paradas autorizadas'}).waitFor();check('Web PARENT consume viaje/GPS reales '+route.direction)
  }
  const eventId=randomUUID(),before=Date.now()
  await request(`/api/v1/trips/${tripId}/boardings`,{method:'POST',token:driver.token,body:{studentId:childId,clientEventId:eventId}})
  const notices=()=>request('/api/v1/notifications?limit=100',{token:parent.token,headers:{'X-Organization-Id':admin.organizationId}})
  await until('Abordaje -> Outbox -> RabbitMQ -> Inbox -> aviso '+route.direction,async()=>{const rows=await notices();return rows.some(n=>n.tripId===tripId&&n.studentId===childId&&n.category==='Boarding')})
  report.boardingToInboxMilliseconds.push({direction:route.direction,milliseconds:Date.now()-before})
  await request(`/api/v1/trips/${tripId}/boardings`,{method:'POST',token:driver.token,body:{studentId:childId,clientEventId:eventId}})
  await sleep(1500);assert.equal((await notices()).filter(n=>n.tripId===tripId&&n.studentId===childId&&n.category==='Boarding').length,1);check('Reintento sin duplicar aviso '+route.direction)
  await request(`/api/v1/trips/${tripId}/sos`,{method:'POST',token:driver.token,body:{clientEventId:randomUUID()}})
  await request(`/api/v1/trips/${tripId}/complete`,{method:'POST',token:driver.token,expected:422,body:{clientEventId:randomUUID()}})
  if(contextAdmin){
   const page=contextAdmin.page;await page.goto(process.env.SPRINT3_WEB_URL+'/admin/trips');const card=page.locator('article').filter({has:page.getByText(`Ruta ${route.direction} ${project}`,{exact:true})});await card.getByRole('button',{name:/Ver viaje|Monitorear|Ver estado|Ver en vivo/}).click();await page.getByRole('button',{name:'Atender y cerrar SOS'}).click();await page.getByRole('button',{name:'Confirmar cierre de SOS'}).click();await page.getByText(/SOS cerrado/).waitFor();await page.screenshot({path:join(out,'admin-'+route.direction+'.png')});check('Web ADMIN cierra SOS real '+route.direction)
  }else await request(`/api/v1/trips/${tripId}/sos/resolve`,{method:'POST',token:admin.token,expected:204})
  const afterSos=await request(`/api/v1/trips/${tripId}/live`,{token:driver.token});assert.equal(afterSos.hasActiveSos,false)
  if(route.direction==='RETURN'){
   const offline={clientEventId:randomUUID(),type:'Alighting',occurredAt:new Date().toISOString(),studentId:childId}
   const applied=await request(`/api/v1/trips/${tripId}/sync`,{method:'POST',token:driver.token,body:{events:[offline]}});assert.equal(applied.applied,1)
   const duplicate=await request(`/api/v1/trips/${tripId}/sync`,{method:'POST',token:driver.token,body:{events:[offline]}});assert.equal(duplicate.duplicates,1)
   const snapshot=await request(`/api/v1/trips/${tripId}/live`,{token:parent.token});assert.equal(snapshot.passengers[0].status,'Alighted');check('Cola offline de retorno y reintento idempotente')
  }
  // Revoke while subscribed; no new GPS may be delivered to that open connection.
  await request(link+'/revoke',{method:'POST',token:admin.token,body:{reference:'Revocación de prueba con canal abierto'}})
  const count=sub.messages.filter(m=>m.target==='PositionUpdated').length
  await sleep(Math.max(0,4000-(Date.now()-Date.parse(reading.recordedAt))))
  const revokedGps=await request('/api/v1/tracking/positions',{method:'POST',token:driver.token,body:{tripId,readings:[{...reading,recordedAt:new Date().toISOString(),latitude:-12.1111}]}});assert.equal(revokedGps.accepted,1)
  await until('Canal real abortado al revocar '+route.direction,async()=>sub.closed,15000)
  await request(`/api/v1/trips/${tripId}/live`,{token:parent.token,expected:403});await sleep(1500);assert.equal(sub.messages.filter(m=>m.target==='PositionUpdated').length,count);check('Revocación REST y canal abierto '+route.direction)
  await request(link+'/verify',{method:'POST',token:admin.token,body:{reference:'Revisión restablecida para cierre de prueba'}})
  await request(`/api/v1/trips/${tripId}/complete`,{method:'POST',token:driver.token,body:{clientEventId:randomUUID()}})
  const ended=await request(`/api/v1/trips/${tripId}/position`,{token:parent.token});assert.equal(ended.position,null)
  await request('/api/v1/tracking/positions',{method:'POST',token:driver.token,expected:422,body:{tripId,readings:[{...reading,recordedAt:new Date().toISOString()}]}});check('Fin de viaje detiene ubicación '+route.direction)
  sub.close()
  if(contextParent){
   const page=contextParent.page;await page.goto(process.env.SPRINT3_WEB_URL+'/parent/tracking');await page.getByLabel('Viaje autorizado',{exact:true}).selectOption(tripId);await page.getByText(/seguimiento de ubicación está detenido/).waitFor();assert.equal(await page.getByRole('region',{name:'Ubicación del vehículo y paradas autorizadas'}).count(),0);await page.screenshot({path:join(out,'parent-'+route.direction+'-completed.png')});check('Web detiene mapa al completar '+route.direction)
  }
 }
 compose('restart','notification')
 await until('Reinicio de Notifications recupera readiness',async()=>{try{return await request('/health/ready')}catch{return false}},90000)
 const inbox=await request('/api/v1/notifications?limit=100',{token:parent.token,headers:{'X-Organization-Id':admin.organizationId}});assert.ok(inbox.length>0);check('Bandeja persistente después del reinicio')
 for(const dependency of ['mysql','rabbitmq','redis']){
  compose('stop',dependency)
  await until('Gateway readiness detecta caída de '+dependency,async()=>{try{return await request('/health/ready',{expected:503})}catch{return false}},90000)
  await request('/health/live');check('Liveness independiente de '+dependency)
  compose('start',dependency)
  await until('Readiness recuperada tras '+dependency,async()=>{try{return await request('/health/ready')}catch{return false}},90000)
 }
 if(contextParent){await contextParent.page.screenshot({path:join(out,'parent-final.png')});report.webVerified=true}
 report.outcome='passed'
}catch(error){report.outcome='failed';report.failure=error.message;console.error('FAILED: '+error.message);process.exitCode=1}
finally{for(const sub of sockets)sub.close();await browser?.close();report.finishedAtUtc=new Date().toISOString();await writeFile(join(out,'report.json'),JSON.stringify(report,null,2))}
