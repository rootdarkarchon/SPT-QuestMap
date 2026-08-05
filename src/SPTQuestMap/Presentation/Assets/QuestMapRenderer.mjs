'use strict';

const VIEW_STORAGE_KEY = 'sptQuestMap.viewport.v2';
const NODE_W = 244, NODE_H = 84, LAYER_GAP = 118, ROW_GAP = 18, LAYOUT_MARGIN = 90, GRID_SIZE = 420;
const REPEATABLE_CARD_GAP = 22, REPEATABLE_GROUP_GAP = 72, REPEATABLE_HEADER_H = 38, REPEATABLE_DIVIDER_GAP = 38;
const OVERVIEW_SCALE = .48, MIN_SCALE = .08, MAX_SCALE = 2.5;
const STATE_THEME_PROPERTIES = {
  Locked:'--qm-state-locked', PrerequisiteGated:'--qm-state-prerequisite-gated', LevelGated:'--qm-state-level-gated',
  TraderGated:'--qm-state-trader-gated', TraderUnavailable:'--qm-state-trader-unavailable', Available:'--qm-state-available',
  InProgress:'--qm-state-in-progress', ReadyToFinish:'--qm-state-ready-to-finish', Completed:'--qm-state-completed',
  Failed:'--qm-state-failed', Excluded:'--qm-state-excluded', RestartableFailure:'--qm-state-restartable-failure',
  Expired:'--qm-state-expired', Pending:'--qm-state-pending'
};
const RENDER_THEME_PROPERTIES = {
  canvasBackground:'--qm-canvas-background', gridDot:'--qm-renderer-grid-dot', edgeFailure:'--qm-edge-failure',
  edgeSuccess:'--qm-edge-success', edgeStarted:'--qm-edge-started', edgeOutcome:'--qm-edge-outcome', edgeOther:'--qm-edge-other',
  cardBody:'--qm-renderer-card-body', bannerShade:'--qm-renderer-banner-shade', eventMarker:'--qm-renderer-event-marker',
  portraitBackground:'--qm-renderer-portrait-background', portraitText:'--qm-renderer-portrait-text', portraitBorder:'--qm-renderer-portrait-border',
  textShadow:'--qm-renderer-text-shadow', titleCompleted:'--qm-renderer-title-completed', title:'--qm-renderer-title',
  titleFuture:'--qm-renderer-title-future', badgeEvent:'--qm-renderer-badge-event', badgeBranch:'--qm-renderer-badge-branch',
  badgeText:'--qm-renderer-badge-text', terminal:'--qm-renderer-terminal', completed:'--qm-renderer-completed', check:'--qm-renderer-check',
  outlineSelected:'--qm-renderer-outline-selected', outlinePrerequisite:'--qm-renderer-outline-prerequisite',
  outlineSuccessor:'--qm-renderer-outline-successor', outlineHover:'--qm-renderer-outline-hover',
  outlineHoverPredecessor:'--qm-renderer-outline-hover-predecessor', outlineHoverSuccessor:'--qm-renderer-outline-hover-successor',
  routeCollector:'--qm-route-collector', routeLightkeeper:'--qm-route-lightkeeper', routeBorder:'--qm-renderer-route-border',
  routeText:'--qm-renderer-route-text', fadeTransparent:'--qm-renderer-fade-transparent', fadeOpaque:'--qm-renderer-fade-opaque',
  mapDivider:'--qm-renderer-map-divider', repeatableDivider:'--qm-renderer-repeatable-divider',
  repeatableTitle:'--qm-renderer-repeatable-title', repeatableTime:'--qm-renderer-repeatable-time'
};

const state = {
  canvas:null, viewport:null, metrics:null, dotnet:null, ctx:null, theme:null, resizeObserver:null, listeners:[],
  topology:null, profile:null, staticNodeById:new Map(), nodeById:new Map(), stateById:new Map(), incoming:new Map(), outgoing:new Map(),
  repeatableGroups:[], repeatableEndById:new Map(), serverTime:0, serverTimeStarted:0, clockTimer:0,
  normalLayout:null, activeLayout:null, applicable:new Set(), visible:new Set(), collectorPath:new Set(), lightkeeperPath:new Set(),
  selectedId:null, focusedId:null, prerequisiteIds:new Set(), successorIds:new Set(), highlightedEdges:new Set(),
  hoverId:null, hoverPredecessorIds:new Set(), hoverSuccessorIds:new Set(), hoveredEdges:new Set(),
  view:{scale:.5,tx:0,ty:0}, dragging:false, dragStart:null, dragMoved:false, pointerDownNode:null,
  fastRenderUntil:0, detailedRenderTimer:0, framePending:false, dpr:1, imageCache:new Map(), mapFadeCache:new Map(),
  firstRenderMs:0, lastRenderMs:0, visibleEdgeCount:0, viewportSaveTimer:0, browserLocale:'en', strings:{}, viewportScope:''
};

export async function initialize(canvas,viewport,metrics,dotnet,snapshot){
  dispose();
  state.canvas=canvas;state.viewport=viewport;state.metrics=metrics;state.dotnet=dotnet;state.ctx=canvas.getContext('2d',{alpha:false});state.theme=readTheme(viewport);
  bindEvents();state.resizeObserver=new ResizeObserver(()=>requestRender());state.resizeObserver.observe(viewport);
  await refresh(snapshot,true);
}

export async function refresh(snapshot,initial=false){
  try{
    const topology=snapshot?.topology,profile=snapshot?.profile??null,config=snapshot?.view;
    if(!topology||!config)throw new Error('QuestMap received an incomplete graph snapshot.');
    const previousVersion=state.topology?.version,previousProfile=state.profile?.profileId;
    applyTopology(topology);applyProfile(profile);applyConfig(config);
    const topologyChanged=previousVersion!==topology.version;
    if(topologyChanged||!state.normalLayout)state.normalLayout=buildLayout(new Set(state.staticNodeById.keys()));
    state.activeLayout=state.normalLayout;rebuildVisible(false);
    const profileChanged=previousProfile!==profile?.profileId;
    if(!restoreViewport(config.viewportScope)&&(initial||profileChanged||topologyChanged))fitVisible();else requestRender();
  }catch(error){await reportFailure(error);throw error;}
}

export function synchronize(config){applyConfig(config);rebuildVisible(true);saveViewport();requestRender();}
export function centerQuest(id){centerNode(id);}
export function fitVisible(){fitGraph();}
export function zoom(factor){if(!state.viewport)return;zoomAt(factor,state.viewport.clientWidth/2,state.viewport.clientHeight/2);}

export function dispose(){
  for(const [target,type,handler,options] of state.listeners)target.removeEventListener(type,handler,options);
  state.listeners=[];state.resizeObserver?.disconnect();state.resizeObserver=null;clearTimeout(state.detailedRenderTimer);clearTimeout(state.viewportSaveTimer);clearInterval(state.clockTimer);state.clockTimer=0;
  state.canvas=null;state.viewport=null;state.metrics=null;state.dotnet=null;state.ctx=null;state.theme=null;state.framePending=false;
}

async function reportFailure(error){try{await state.dotnet?.invokeMethodAsync('OnRendererFailed',error?.message||String(error));}catch{}}

function readTheme(element){
  const styles=getComputedStyle(element),read=property=>{const value=styles.getPropertyValue(property).trim();if(!value)throw new Error(`QuestMap renderer theme is missing ${property}.`);return value;};
  return{...Object.fromEntries(Object.entries(RENDER_THEME_PROPERTIES).map(([key,property])=>[key,read(property)])),states:Object.fromEntries(Object.entries(STATE_THEME_PROPERTIES).map(([key,property])=>[key,read(property)]))};
}

function applyTopology(topology){
  state.topology=topology;state.staticNodeById=new Map(topology.quests.map(node=>[node.id,node]));state.nodeById=new Map(state.staticNodeById);
  state.collectorPath=new Set(topology.collectorPathQuestIds||[]);state.lightkeeperPath=new Set(topology.lightkeeperPathQuestIds||[]);
  state.incoming=groupEdges(topology.edges,'targetId');state.outgoing=groupEdges(topology.edges,'sourceId');
}
function applyProfile(profile){
  state.profile=profile;state.nodeById=new Map(state.staticNodeById);state.stateById=new Map((profile?.quests||[]).map(item=>[item.questId,item]));state.applicable=new Set(profile?.allApplicableQuestIds||[]);
  state.repeatableGroups=profile?.repeatableQuestGroups||[];state.repeatableEndById=new Map();
  for(const group of state.repeatableGroups)for(const entry of group.quests||[]){state.nodeById.set(entry.node.id,entry.node);state.stateById.set(entry.state.questId,entry.state);state.applicable.add(entry.node.id);state.repeatableEndById.set(entry.node.id,group.endTime);}
  state.serverTime=profile?.generatedAt||Math.floor(Date.now()/1000);state.serverTimeStarted=performance.now();
}
function applyConfig(config){
  if(config.browserLocale)state.browserLocale=config.browserLocale;if(config.strings)state.strings=config.strings;state.viewportScope=config.viewportScope||'';
  state.visible=new Set(config.visibleQuestIds||[]);state.selectedId=config.selectedId||null;state.focusedId=config.focusedId||null;
  state.prerequisiteIds=new Set(config.prerequisiteIds||[]);state.successorIds=new Set(config.successorIds||[]);state.highlightedEdges=new Set(config.highlightedEdgeIndexes||[]);
}
function groupEdges(edges,key){const groups=new Map();edges.forEach((edge,index)=>{const value=edge[key];if(!groups.has(value))groups.set(value,[]);groups.get(value).push({edge,index});});return groups;}

function buildLayout(idSet){
  const started=performance.now(),idList=[...idSet].filter(id=>state.nodeById.has(id)),ids=new Set(idList),memo=new Map(),visiting=new Set();
  const rankOf=id=>{if(memo.has(id))return memo.get(id);if(visiting.has(id))return 0;visiting.add(id);let rank=0;for(const item of state.incoming.get(id)||[])if(ids.has(item.edge.sourceId))rank=Math.max(rank,rankOf(item.edge.sourceId)+1);visiting.delete(id);memo.set(id,Math.min(rank,80));return memo.get(id);};
  const layers=new Map();for(const id of idList){const rank=rankOf(id);if(!layers.has(rank))layers.set(rank,[]);layers.get(rank).push(id);}
  const positions=new Map();for(const [rank,layer] of [...layers].sort((a,b)=>a[0]-b[0])){layer.sort((a,b)=>{const qa=state.nodeById.get(a),qb=state.nodeById.get(b);return qa.traderName.localeCompare(qb.traderName)||qa.name.localeCompare(qb.name)||a.localeCompare(b);});layer.forEach((id,row)=>positions.set(id,{x:LAYOUT_MARGIN+rank*(NODE_W+LAYER_GAP),y:LAYOUT_MARGIN+row*(NODE_H+ROW_GAP),w:NODE_W,h:NODE_H}));}
  return finishLayout(positions,started);
}
function buildCompactedLayout(idSet,referenceLayout){
  const started=performance.now(),columns=new Map();for(const id of idSet){const p=referenceLayout?.positions.get(id);if(!p)continue;if(!columns.has(p.x))columns.set(p.x,[]);columns.get(p.x).push({id,y:p.y});}
  const positions=new Map();let column=0;for(const [,items] of [...columns].sort((a,b)=>a[0]-b[0])){items.sort((a,b)=>a.y-b.y||a.id.localeCompare(b.id));items.forEach((item,row)=>positions.set(item.id,{x:LAYOUT_MARGIN+column*(NODE_W+LAYER_GAP),y:LAYOUT_MARGIN+row*(NODE_H+ROW_GAP),w:NODE_W,h:NODE_H}));column++;}
  return finishLayout(positions,started);
}
function finishLayout(positions,started){
  const edgeGeometry=[];state.topology.edges.forEach((edge,index)=>{const source=positions.get(edge.sourceId),target=positions.get(edge.targetId);if(!source||!target)return;const x1=source.x+source.w,y1=source.y+source.h/2,x2=target.x,y2=target.y+target.h/2,bend=Math.max(42,Math.abs(x2-x1)*.42);const distance=Math.round((target.x-source.x)/(NODE_W+LAYER_GAP)),skipped=Math.max(0,distance-1),horizontal=Math.abs(y2-y1)<1,arch=skipped&&horizontal?Math.min(64,18+skipped*10):0;edgeGeometry.push({index,edge,x1,y1,x2,y2,c1x:x1+bend,c1y:y1-arch,c2x:x2-bend,c2y:y2-arch});});
  const values=[...positions.values()],width=Math.max(800,...values.map(p=>p.x+p.w+LAYOUT_MARGIN)),height=Math.max(600,...values.map(p=>p.y+p.h+LAYOUT_MARGIN));
  const nodeGrid=buildGrid([...positions].map(([id,p])=>({id,minX:p.x,minY:p.y,maxX:p.x+p.w,maxY:p.y+p.h}))),edgeGrid=buildGrid(edgeGeometry.map(g=>({id:g.index,minX:Math.min(g.x1,g.x2,g.c1x,g.c2x),minY:Math.min(g.y1,g.y2,g.c1y,g.c2y),maxX:Math.max(g.x1,g.x2,g.c1x,g.c2x),maxY:Math.max(g.y1,g.y2,g.c1y,g.c2y)})));
  return{positions,edgeGeometry,edgeByIndex:new Map(edgeGeometry.map(g=>[g.index,g])),nodeGrid,edgeGrid,width,height,layoutMs:performance.now()-started};
}
function buildGrid(items){const grid=new Map();for(const item of items){const x0=Math.floor(item.minX/GRID_SIZE),x1=Math.floor(item.maxX/GRID_SIZE),y0=Math.floor(item.minY/GRID_SIZE),y1=Math.floor(item.maxY/GRID_SIZE);for(let x=x0;x<=x1;x++)for(let y=y0;y<=y1;y++){const key=`${x},${y}`;if(!grid.has(key))grid.set(key,[]);grid.get(key).push(item.id);}}return grid;}
function queryGrid(grid,rect){const result=new Set(),x0=Math.floor(rect.minX/GRID_SIZE),x1=Math.floor(rect.maxX/GRID_SIZE),y0=Math.floor(rect.minY/GRID_SIZE),y1=Math.floor(rect.maxY/GRID_SIZE);for(let x=x0;x<=x1;x++)for(let y=y0;y<=y1;y++)for(const id of grid.get(`${x},${y}`)||[])result.add(id);return result;}
function rebuildVisible(preserveAnchor){
  if(!state.normalLayout)return;const matches=state.activeLayout&&state.activeLayout.positions.size===state.visible.size&&[...state.visible].every(id=>state.activeLayout.positions.has(id));
  if(!matches){const anchor=preserveAnchor?chooseLayoutAnchor():null,staticVisible=new Set([...state.visible].filter(id=>state.staticNodeById.has(id))),allStatic=staticVisible.size===state.normalLayout.positions.size&&[...staticVisible].every(id=>state.normalLayout.positions.has(id));const base=allStatic?state.normalLayout:buildCompactedLayout(staticVisible,state.normalLayout);state.activeLayout=composeRepeatableLayout(base);if(anchor)restoreAnchor(anchor.id,anchor.screen);updateClockTimer();}
  state.visibleEdgeCount=0;for(const {edge} of state.activeLayout.edgeGeometry)if(state.visible.has(edge.sourceId)&&state.visible.has(edge.targetId))state.visibleEdgeCount++;requestRender();
}
function composeRepeatableLayout(base){
  const groups=state.repeatableGroups.map(group=>({kind:group.kind,endTime:group.endTime,entries:(group.quests||[]).filter(entry=>state.visible.has(entry.node.id))})).filter(group=>group.entries.length);
  if(!groups.length)return{...base,repeatableBands:[],repeatableDivider:null};
  const started=performance.now(),nodeY=LAYOUT_MARGIN+REPEATABLE_HEADER_H,dividerY=nodeY+NODE_H+REPEATABLE_DIVIDER_GAP,normalOffset=dividerY+52-LAYOUT_MARGIN,positions=new Map();
  for(const [id,p] of base.positions)positions.set(id,{...p,y:p.y+normalOffset});
  const bands=[];let x=LAYOUT_MARGIN;
  for(const group of groups){const startX=x;for(const entry of group.entries){positions.set(entry.node.id,{x,y:nodeY,w:NODE_W,h:NODE_H});x+=NODE_W+REPEATABLE_CARD_GAP;}x-=REPEATABLE_CARD_GAP;bands.push({kind:group.kind,endTime:group.endTime,x1:startX,x2:x,y:LAYOUT_MARGIN+9});x+=REPEATABLE_GROUP_GAP;}
  const layout=finishLayout(positions,started),baseRight=Math.max(LAYOUT_MARGIN,...[...base.positions.values()].map(p=>p.x+p.w));layout.repeatableBands=bands;layout.repeatableDivider={x1:LAYOUT_MARGIN,x2:Math.max(baseRight,bands.at(-1).x2),y:dividerY};return layout;
}
function updateClockTimer(){clearInterval(state.clockTimer);state.clockTimer=state.activeLayout?.repeatableBands?.length?setInterval(requestRender,1000):0;}
function chooseLayoutAnchor(){if(!state.activeLayout||!state.visible.size)return null;for(const id of [state.selectedId,state.focusedId])if(id&&state.visible.has(id)&&state.activeLayout.positions.has(id))return{id,screen:screenPosition(id)};const cx=state.viewport.clientWidth/2,cy=state.viewport.clientHeight/2;let best=null,distance=Infinity;for(const id of state.visible){const p=state.activeLayout.positions.get(id);if(!p)continue;const x=state.view.tx+(p.x+p.w/2)*state.view.scale,y=state.view.ty+(p.y+p.h/2)*state.view.scale,d=(x-cx)**2+(y-cy)**2;if(d<distance){distance=d;best={id,screen:{x,y}};}}return best;}
function screenPosition(id){const p=id&&state.activeLayout?.positions.get(id);return p?{x:state.view.tx+(p.x+p.w/2)*state.view.scale,y:state.view.ty+(p.y+p.h/2)*state.view.scale}:null;}
function restoreAnchor(id,anchor){const p=id&&state.activeLayout?.positions.get(id);if(!p||!anchor)return;state.view.tx=anchor.x-(p.x+p.w/2)*state.view.scale;state.view.ty=anchor.y-(p.y+p.h/2)*state.view.scale;}

function requestRender(){if(state.framePending||!state.canvas)return;state.framePending=true;requestAnimationFrame(render);}
function render(){
  state.framePending=false;if(!state.ctx||!state.viewport)return;const started=performance.now();resizeCanvasBacking();const width=state.viewport.clientWidth,height=state.viewport.clientHeight,ctx=state.ctx;
  ctx.setTransform(state.dpr,0,0,state.dpr,0,0);ctx.fillStyle=state.theme.canvasBackground;ctx.fillRect(0,0,width,height);if(!state.activeLayout||!state.profile)return;
  const rect={minX:-state.view.tx/state.view.scale-NODE_W,minY:-state.view.ty/state.view.scale-NODE_H,maxX:(width-state.view.tx)/state.view.scale+NODE_W,maxY:(height-state.view.ty)/state.view.scale+NODE_H};
  drawGrid(width,height);ctx.setTransform(state.dpr*state.view.scale,0,0,state.dpr*state.view.scale,state.dpr*state.view.tx,state.dpr*state.view.ty);
  drawRepeatableBands();const edgeIds=queryGrid(state.activeLayout.edgeGrid,rect),hasSelection=Boolean(state.selectedId),fast=state.dragging||performance.now()<state.fastRenderUntil;
  if(!fast)for(const index of edgeIds){const g=state.activeLayout.edgeByIndex.get(index);if(!g||!state.visible.has(g.edge.sourceId)||!state.visible.has(g.edge.targetId))continue;const selected=state.highlightedEdges.has(index),hover=state.hoveredEdges.has(index),sourceState=effectiveQuestState(g.edge.sourceId)?.displayState;drawEdge(g,hasSelection&&!selected&&!hover,selected||hover,isFutureQuest(effectiveQuestState(g.edge.targetId))&&state.selectedId!==g.edge.targetId&&state.hoverId!==g.edge.targetId,sourceState);}
  for(const id of queryGrid(state.activeLayout.nodeGrid,rect)){if(!state.visible.has(id))continue;const p=state.activeLayout.positions.get(id),hoverRelated=id===state.hoverId||state.hoverPredecessorIds.has(id)||state.hoverSuccessorIds.has(id),selectionRelated=id===state.selectedId||state.prerequisiteIds.has(id)||state.successorIds.has(id);drawNode(state.nodeById.get(id),effectiveQuestState(id),p,hasSelection&&!selectionRelated&&!hoverRelated);}
  state.lastRenderMs=performance.now()-started;if(!state.firstRenderMs)state.firstRenderMs=state.lastRenderMs;updateMetrics();
}
function drawGrid(width,height){const spacing=38*state.view.scale;if(spacing<11)return;const ctx=state.ctx;ctx.fillStyle=state.theme.gridDot;const ox=((state.view.tx%spacing)+spacing)%spacing,oy=((state.view.ty%spacing)+spacing)%spacing;for(let x=ox;x<width;x+=spacing)for(let y=oy;y<height;y+=spacing)ctx.fillRect(x,y,1,1);}
function drawRepeatableBands(){
  const layout=state.activeLayout;if(!layout?.repeatableBands?.length)return;const ctx=state.ctx,divider=layout.repeatableDivider;ctx.save();ctx.textAlign='center';
  for(const band of layout.repeatableBands){const remaining=Math.floor(band.endTime-currentServerTime()),expired=remaining<=0,label=t(band.kind==='Daily'?'repeatable.daily':'repeatable.weekly'),time=expired?t('repeatable.expired'):`${formatRemaining(remaining)} ${t('repeatable.remaining')}`;ctx.fillStyle=state.theme.repeatableTitle;ctx.font='700 15px Georgia, serif';ctx.fillText(label,(band.x1+band.x2)/2,band.y);ctx.fillStyle=state.theme.repeatableTime;ctx.font='600 10px system-ui';ctx.fillText(time,(band.x1+band.x2)/2,band.y+16);}
  if(divider){ctx.strokeStyle=state.theme.repeatableDivider;ctx.lineWidth=1.5/state.view.scale;ctx.beginPath();ctx.moveTo(divider.x1,divider.y);ctx.lineTo(divider.x2,divider.y);ctx.stroke();}ctx.restore();
}
function currentServerTime(){return state.serverTime+(performance.now()-state.serverTimeStarted)/1000;}
function formatRemaining(value){const total=Math.max(0,Math.floor(value)),days=Math.floor(total/86400),hours=Math.floor(total%86400/3600),minutes=Math.floor(total%3600/60),seconds=total%60;if(days)return`${days}d ${hours}h`;if(hours)return`${hours}h ${minutes}m`;if(minutes)return`${minutes}m ${seconds}s`;return`${seconds}s`;}
function effectiveQuestState(id){const questState=state.stateById.get(id),endTime=state.repeatableEndById.get(id);return questState&&endTime!=null&&endTime<=currentServerTime()&&questState.displayState!=='Expired'?{...questState,displayState:'Expired'}:questState;}
function edgeRequirementKind(edge){if(edge.requirementKind)return edge.requirementKind;const values=edge.requiredStatuses||[],started=values.includes('Started'),success=values.includes('Success'),failure=values.some(value=>value.includes('Fail'));return started?'Started':success&&failure?'AnyOutcome':failure?'Failure':success?'Success':'Other';}
function edgeColor(edge){const kind=edgeRequirementKind(edge);return kind==='Failure'?state.theme.edgeFailure:kind==='Success'?state.theme.edgeSuccess:kind==='Started'?state.theme.edgeStarted:kind==='AnyOutcome'?state.theme.edgeOutcome:state.theme.edgeOther;}
function drawEdge(g,dimmed,highlighted,futureTarget,sourceState){const ctx=state.ctx,completed=sourceState==='Completed',active=['Available','InProgress','ReadyToFinish'].includes(sourceState);ctx.save();ctx.globalAlpha=highlighted?(completed?(futureTarget?.28:.48):(futureTarget?.38:.96)):dimmed?(active?.32:.08):completed?.20:active?.64:futureTarget?.18:.42;ctx.strokeStyle=edgeColor(g.edge);ctx.lineWidth=(highlighted?3.2:1.5)/state.view.scale;if(edgeRequirementKind(g.edge)==='Failure')ctx.setLineDash([8/state.view.scale,5/state.view.scale]);ctx.beginPath();ctx.moveTo(g.x1,g.y1);ctx.bezierCurveTo(g.c1x,g.c1y,g.c2x,g.c2y,g.x2,g.y2);ctx.stroke();const size=7/state.view.scale;ctx.setLineDash([]);ctx.fillStyle=ctx.strokeStyle;ctx.beginPath();ctx.moveTo(g.x2,g.y2);ctx.lineTo(g.x2-size,g.y2-size*.62);ctx.lineTo(g.x2-size,g.y2+size*.62);ctx.closePath();ctx.fill();ctx.restore();}
function drawNode(node,questState,p,dimmed){
  const ctx=state.ctx,display=questState?.displayState||'Locked',color=state.theme.states[display]||state.theme.states.Locked,selected=node.id===state.selectedId,future=isFutureQuest(questState),completed=display==='Completed';
  const prerequisite=state.prerequisiteIds.has(node.id),successor=state.successorIds.has(node.id),hovered=node.id===state.hoverId,hoverPredecessor=state.hoverPredecessorIds.has(node.id),hoverSuccessor=state.hoverSuccessorIds.has(node.id),emphasized=selected||prerequisite||successor||hovered||hoverPredecessor||hoverSuccessor,nodeAlpha=dimmed?.20:future&&!emphasized?.56:1,body={x:p.x+6,y:p.y,w:p.w-6,h:p.h},hasRoute=state.collectorPath.has(node.id)||state.lightkeeperPath.has(node.id);
  ctx.save();ctx.globalAlpha=nodeAlpha;roundRect(p.x,p.y,p.w,p.h,8);ctx.fillStyle=color;ctx.fill();roundRect(body.x,body.y,body.w,body.h,8);ctx.fillStyle=state.theme.cardBody;ctx.fill();ctx.save();roundRect(body.x,body.y,body.w,body.h,8);ctx.clip();ctx.globalAlpha=1;const hasBanner=drawCardBanner(node,body);if(hasBanner){ctx.fillStyle=state.theme.bannerShade;ctx.fillRect(body.x,body.y,body.w,body.h);}ctx.restore();drawRouteStrip(node.id,body);
  if(state.view.scale<OVERVIEW_SCALE){ctx.fillStyle=color;roundRect(body.x+10,p.y+17,Math.max(18,body.w-26),p.h-34,4);ctx.fill();if(node.eventSeason){ctx.fillStyle=state.theme.eventMarker;ctx.fillRect(p.x+p.w-12,p.y+7,5,5);}if(isTerminalQuest(node.id))drawTerminalMarker(p);if(completed)drawCompletedMarker(p);drawNodeOutline(p,selected,prerequisite,successor,hovered,hoverPredecessor,hoverSuccessor);if(hasDirectPrerequisite(node.id))drawCenterNotch(p);ctx.restore();return;}
  const iconSize=51,iconX=body.x+9,iconY=p.y+(hasRoute?8.5:(p.h-iconSize)/2),traderImage=getImage(node.traderImageUrl);if(traderImage?.complete&&traderImage.naturalWidth){ctx.save();roundRect(iconX,iconY,iconSize,iconSize,5);ctx.clip();drawImageCover(traderImage,iconX,iconY,iconSize,iconSize);ctx.restore();}else{ctx.fillStyle=state.theme.portraitBackground;roundRect(iconX,iconY,iconSize,iconSize,5);ctx.fill();ctx.fillStyle=state.theme.portraitText;ctx.font='700 16px system-ui';ctx.textAlign='center';ctx.fillText(initials(node.traderName),iconX+iconSize/2,iconY+32);}ctx.strokeStyle=state.theme.portraitBorder;ctx.lineWidth=1/state.view.scale;roundRect(iconX,iconY,iconSize,iconSize,5);ctx.stroke();
  const textX=iconX+iconSize+9,textWidth=p.x+p.w-13-textX;ctx.save();ctx.shadowColor=state.theme.textShadow;ctx.shadowBlur=3;ctx.shadowOffsetX=1;ctx.shadowOffsetY=1;ctx.textAlign='left';ctx.fillStyle=completed?state.theme.titleCompleted:state.theme.title;ctx.font=`${completed?600:700} 13px system-ui`;const titleLines=wrapText(node.name,textWidth,2),titleY=iconY+(titleLines.length===1?19:11);for(let i=0;i<titleLines.length;i++)ctx.fillText(titleLines[i],textX,titleY+i*16);ctx.fillStyle=future&&!selected?state.theme.titleFuture:color;ctx.font='700 9px system-ui';const progress=display==='InProgress'&&questState?.progressPercent!=null?` · ~${number(questState.progressPercent)}%`:'';ctx.fillText(`${stateLabel(display).toLocaleUpperCase(state.browserLocale)}${progress}`,textX,iconY+49);ctx.restore();
  if(node.eventSeason)badge(p.x+p.w-(completed?36:13),p.y+12,t('badge.event'),state.theme.badgeEvent);else if(node.exclusionRules?.length)badge(p.x+p.w-(completed?36:13),p.y+12,t('badge.branch'),state.theme.badgeBranch);if(isTerminalQuest(node.id))drawTerminalMarker(p);if(completed)drawCompletedMarker(p);drawNodeOutline(p,selected,prerequisite,successor,hovered,hoverPredecessor,hoverSuccessor);if(hasDirectPrerequisite(node.id))drawCenterNotch(p);ctx.restore();
}
function roundRect(x,y,w,h,r){state.ctx.beginPath();state.ctx.roundRect(x,y,w,h,r);}
function isTerminalQuest(id){return!state.repeatableEndById.has(id)&&!(state.outgoing.get(id)||[]).some(item=>state.applicable.has(item.edge.targetId));}
function hasDirectPrerequisite(id){return(state.incoming.get(id)||[]).some(item=>state.applicable.has(item.edge.sourceId));}
function drawTerminalMarker(p){const ctx=state.ctx;ctx.save();roundRect(p.x,p.y,p.w,p.h,8);ctx.clip();ctx.fillStyle=state.theme.terminal;roundRect(p.x+p.w-5,p.y+9,4,p.h-18,2);ctx.fill();ctx.restore();}
function drawCompletedMarker(p){const ctx=state.ctx,right=p.x+p.w,top=p.y,size=38,cx=right-size/3,cy=top+size/3;ctx.save();roundRect(p.x,p.y,p.w,p.h,8);ctx.clip();ctx.fillStyle=state.theme.completed;ctx.beginPath();ctx.moveTo(right-size,top);ctx.lineTo(right,top);ctx.lineTo(right,top+size);ctx.closePath();ctx.fill();ctx.translate(cx,cy);ctx.fillStyle=state.theme.check;ctx.beginPath();ctx.moveTo(-7,0);ctx.lineTo(-4,-3);ctx.lineTo(-1,0);ctx.lineTo(6,-7);ctx.lineTo(9,-4);ctx.lineTo(-1,6);ctx.closePath();ctx.fill();ctx.restore();}
function drawCenterNotch(p){const ctx=state.ctx,cy=p.y+p.h/2;ctx.save();ctx.globalAlpha=1;ctx.fillStyle=state.theme.canvasBackground;ctx.beginPath();ctx.moveTo(p.x,cy-4);ctx.lineTo(p.x+5,cy);ctx.lineTo(p.x,cy+4);ctx.closePath();ctx.fill();ctx.restore();}
function drawNodeOutline(p,selected,prerequisite,successor,hovered,hoverPredecessor,hoverSuccessor){if(!selected&&!prerequisite&&!successor&&!hovered&&!hoverPredecessor&&!hoverSuccessor)return;const ctx=state.ctx;ctx.save();ctx.lineWidth=(selected?4:prerequisite||successor?2.5:2)/state.view.scale;ctx.strokeStyle=selected?state.theme.outlineSelected:prerequisite?state.theme.outlinePrerequisite:successor?state.theme.outlineSuccessor:hovered?state.theme.outlineHover:hoverPredecessor?state.theme.outlineHoverPredecessor:state.theme.outlineHoverSuccessor;roundRect(p.x,p.y,p.w,p.h,8);ctx.stroke();ctx.restore();}
function drawRouteStrip(id,p){const collector=state.collectorPath.has(id),lightkeeper=state.lightkeeperPath.has(id);if(!collector&&!lightkeeper)return;const ctx=state.ctx,x=p.x+9,y=p.y+67.5,w=p.w-18,h=8;ctx.save();roundRect(p.x,p.y,p.w,p.h,8);ctx.clip();roundRect(x,y,w,h,h/2);ctx.clip();if(collector&&lightkeeper){routeSegment(x,y,w/2,h,state.theme.routeCollector,t('route.collector'));routeSegment(x+w/2,y,w/2,h,state.theme.routeLightkeeper,t('route.lightkeeper'));}else if(collector)routeSegment(x,y,w,h,state.theme.routeCollector,t('route.collector'));else routeSegment(x,y,w,h,state.theme.routeLightkeeper,t('route.lightkeeper'));ctx.restore();ctx.save();roundRect(p.x,p.y,p.w,p.h,8);ctx.clip();ctx.strokeStyle=state.theme.routeBorder;ctx.lineWidth=.8/state.view.scale;roundRect(x,y,w,h,h/2);ctx.stroke();ctx.restore();}
function routeSegment(x,y,w,h,color,label){const ctx=state.ctx;ctx.fillStyle=color;ctx.fillRect(x,y,w,h);if(state.view.scale<OVERVIEW_SCALE)return;ctx.fillStyle=state.theme.routeText;ctx.font='700 6.5px system-ui';ctx.textAlign='center';ctx.fillText(label,x+w/2,y+6.2);}
function drawCardBanner(node,body){const quest=getImage(node.imageUrl),mapUrl=node.location?.bannerImageUrl,map=getImage(mapUrl),questReady=Boolean(quest?.complete&&quest.naturalWidth),mapReady=Boolean(map?.complete&&map.naturalWidth);if(!questReady&&!mapReady)return false;if(questReady)drawImageCover(quest,body.x,body.y,body.w,body.h);else drawImageCover(map,body.x,body.y,body.w,body.h);if(questReady&&mapReady)state.ctx.drawImage(getMapFadeLayer(mapUrl,map,body.w,body.h),body.x,body.y,body.w,body.h);return true;}
function getMapFadeLayer(mapUrl,map,width,height){const key=`${mapUrl}|${width}x${height}`;if(state.mapFadeCache.has(key))return state.mapFadeCache.get(key);const layer=document.createElement('canvas');layer.width=Math.ceil(width);layer.height=Math.ceil(height);const ctx=layer.getContext('2d'),angle=Math.PI/6,nx=Math.cos(angle),ny=Math.sin(angle),centerX=layer.width*2/3,centerY=layer.height/2,halfWidth=7.5,minimumVisibleX=centerX-Math.tan(angle)*layer.height/2-nx*halfWidth,mapCenterX=(centerX+layer.width)/2,mapHalfSpan=Math.max(mapCenterX-minimumVisibleX,layer.width-mapCenterX);drawImageCoverTo(ctx,map,mapCenterX-mapHalfSpan,0,mapHalfSpan*2,layer.height);ctx.globalCompositeOperation='destination-in';const fade=ctx.createLinearGradient(centerX-nx*halfWidth,centerY-ny*halfWidth,centerX+nx*halfWidth,centerY+ny*halfWidth);fade.addColorStop(0,state.theme.fadeTransparent);fade.addColorStop(1,state.theme.fadeOpaque);ctx.fillStyle=fade;ctx.fillRect(0,0,layer.width,layer.height);const tx=-ny,ty=nx,length=Math.hypot(layer.width,layer.height);ctx.globalCompositeOperation='source-over';ctx.strokeStyle=state.theme.mapDivider;ctx.lineWidth=1.5;ctx.beginPath();ctx.moveTo(centerX-tx*length,centerY-ty*length);ctx.lineTo(centerX+tx*length,centerY+ty*length);ctx.stroke();state.mapFadeCache.set(key,layer);return layer;}
function drawImageCoverTo(target,image,x,y,w,h){const sourceWidth=image.naturalWidth||image.width,sourceHeight=image.naturalHeight||image.height,scale=Math.max(w/sourceWidth,h/sourceHeight),sw=w/scale,sh=h/scale,sx=(sourceWidth-sw)/2,sy=(sourceHeight-sh)/2;target.drawImage(image,sx,sy,sw,sh,x,y,w,h);}
function drawImageCover(image,x,y,w,h){drawImageCoverTo(state.ctx,image,x,y,w,h);}
function badge(x,y,text,color){const ctx=state.ctx;ctx.fillStyle=color;ctx.beginPath();ctx.arc(x,y,9,0,Math.PI*2);ctx.fill();ctx.fillStyle=state.theme.badgeText;ctx.font='800 9px system-ui';ctx.textAlign='center';ctx.fillText(text,x,y+3);}
function getImage(url){if(!url)return null;if(state.imageCache.has(url))return state.imageCache.get(url);const image=new Image();image.decoding='async';image.onload=()=>{state.mapFadeCache.clear();requestRender();};image.onerror=()=>{state.imageCache.set(url,null);state.mapFadeCache.clear();};image.src=url;state.imageCache.set(url,image);return image;}
function isFutureQuest(questState){return Boolean(questState)&&!questState.inProfile&&!questState.authoritativelyVisible;}
function initials(value){return value.split(/\s+/).slice(0,2).map(part=>part[0]||'').join('').toUpperCase();}
function t(key,values={}){const template=state.strings[key]??key;return template.replace(/\{([a-zA-Z0-9_]+)\}/g,(_,name)=>values[name]??`{${name}}`);}
function stateLabel(value){return t(`state.${value}`);}
function number(value){return value==null?'?':Number(value).toLocaleString(state.browserLocale,{maximumFractionDigits:2});}
function fitText(value,width){if(state.ctx.measureText(value).width<=width)return value;let low=0,high=value.length;while(low<high){const mid=Math.ceil((low+high)/2);if(state.ctx.measureText(`${value.slice(0,mid)}…`).width<=width)low=mid;else high=mid-1;}return`${value.slice(0,low)}…`;}
function wrapText(value,width,maxLines){const words=value.trim().split(/\s+/),lines=[];let current='';for(let index=0;index<words.length;index++){const candidate=current?`${current} ${words[index]}`:words[index];if(!current||state.ctx.measureText(candidate).width<=width){current=candidate;continue;}lines.push(fitText(current,width));current=words[index];if(lines.length===maxLines-1){current=[current,...words.slice(index+1)].join(' ');break;}}if(current&&lines.length<maxLines)lines.push(fitText(current,width));return lines.length?lines:[''];}
function resizeCanvasBacking(){const dpr=Math.min(2,window.devicePixelRatio||1),width=Math.max(1,Math.round(state.viewport.clientWidth*dpr)),height=Math.max(1,Math.round(state.viewport.clientHeight*dpr));if(state.canvas.width!==width||state.canvas.height!==height){state.canvas.width=width;state.canvas.height=height;state.dpr=dpr;}}

function fitGraph(){if(!state.visible.size||!state.activeLayout||!state.viewport)return;const positions=[...state.visible].map(id=>state.activeLayout.positions.get(id)).filter(Boolean);if(!positions.length)return;const minX=Math.min(...positions.map(p=>p.x)),minY=Math.min(...positions.map(p=>p.y)),maxX=Math.max(...positions.map(p=>p.x+p.w)),maxY=Math.max(...positions.map(p=>p.y+p.h)),w=state.viewport.clientWidth,h=state.viewport.clientHeight,inset=drawerInset(),available=w-inset;state.view.scale=clamp(Math.min((available-80)/Math.max(1,maxX-minX),(h-90)/Math.max(1,maxY-minY)),MIN_SCALE,1.05);state.view.tx=inset+(available-(minX+maxX)*state.view.scale)/2;state.view.ty=(h-(minY+maxY)*state.view.scale)/2;saveViewport();requestRender();}
function centerNode(id){const p=state.activeLayout?.positions.get(id);if(!p||!state.viewport)return;const inset=drawerInset(),centerX=inset+(state.viewport.clientWidth-inset)/2;state.view.tx=centerX-(p.x+p.w/2)*state.view.scale;state.view.ty=state.viewport.clientHeight/2-(p.y+p.h/2)*state.view.scale;saveViewport();requestRender();}
function drawerInset(){const panel=state.viewport?.querySelector('.in-progress-panel.expanded');return panel?panel.getBoundingClientRect().width+12:0;}
function beginFastRender(duration=150){state.fastRenderUntil=performance.now()+duration;clearTimeout(state.detailedRenderTimer);state.detailedRenderTimer=setTimeout(()=>{state.fastRenderUntil=0;requestRender();},duration+24);}
function zoomAt(factor,sx,sy){const next=clamp(state.view.scale*factor,MIN_SCALE,MAX_SCALE),worldX=(sx-state.view.tx)/state.view.scale,worldY=(sy-state.view.ty)/state.view.scale;state.view.scale=next;state.view.tx=sx-worldX*next;state.view.ty=sy-worldY*next;scheduleViewportSave();requestRender();}
function scheduleViewportSave(){clearTimeout(state.viewportSaveTimer);state.viewportSaveTimer=setTimeout(saveViewport,160);}
function saveViewport(){if(!state.viewportScope)return;try{localStorage.setItem(VIEW_STORAGE_KEY,JSON.stringify({scope:state.viewportScope,view:{...state.view}}));}catch{}}
function restoreViewport(scope){try{const saved=JSON.parse(localStorage.getItem(VIEW_STORAGE_KEY));if(saved?.scope!==scope||!validView(saved.view))return false;state.view={...saved.view};return true;}catch{return false;}}
function validView(view){return Number.isFinite(view?.scale)&&Number.isFinite(view?.tx)&&Number.isFinite(view?.ty)&&view.scale>=MIN_SCALE&&view.scale<=MAX_SCALE;}
function clamp(value,min,max){return Math.max(min,Math.min(max,value));}
function worldAt(event){const rect=state.canvas.getBoundingClientRect();return{x:(event.clientX-rect.left-state.view.tx)/state.view.scale,y:(event.clientY-rect.top-state.view.ty)/state.view.scale};}
function hitNode(event){if(!state.activeLayout)return null;const point=worldAt(event),key=`${Math.floor(point.x/GRID_SIZE)},${Math.floor(point.y/GRID_SIZE)}`,candidates=state.activeLayout.nodeGrid.get(key)||[];for(let i=candidates.length-1;i>=0;i--){const id=candidates[i],p=state.activeLayout.positions.get(id);if(state.visible.has(id)&&point.x>=p.x&&point.x<=p.x+p.w&&point.y>=p.y&&point.y<=p.y+p.h)return id;}return null;}
function updateHover(id){state.hoverId=id;state.hoverPredecessorIds=new Set();state.hoverSuccessorIds=new Set();state.hoveredEdges=new Set();if(!id)return;for(const item of state.incoming.get(id)||[])if(state.applicable.has(item.edge.sourceId)){state.hoverPredecessorIds.add(item.edge.sourceId);state.hoveredEdges.add(item.index);}for(const item of state.outgoing.get(id)||[])if(state.applicable.has(item.edge.targetId)){state.hoverSuccessorIds.add(item.edge.targetId);state.hoveredEdges.add(item.index);}}
function updateMetrics(){if(!state.metrics||!state.topology||!state.profile||!state.activeLayout)return;state.metrics.textContent=t('metrics.summary',{visible:state.visible.size,applicable:state.applicable.size,edges:state.visibleEdgeCount,layout:state.activeLayout.layoutMs.toFixed(1),overlay:'0.0',render:state.lastRenderMs.toFixed(1)})+(state.view.scale<OVERVIEW_SCALE?t('metrics.overview'):'');}

function bindEvents(){
  const on=(target,type,handler,options)=>{target.addEventListener(type,handler,options);state.listeners.push([target,type,handler,options]);};
  on(state.canvas,'wheel',event=>{event.preventDefault();beginFastRender();const rect=state.canvas.getBoundingClientRect();zoomAt(event.deltaY<0?1.13:1/1.13,event.clientX-rect.left,event.clientY-rect.top);},{passive:false});
  on(state.canvas,'pointerdown',event=>{if(event.button!==0)return;state.pointerDownNode=hitNode(event);state.dragging=true;state.dragMoved=false;state.dragStart={x:event.clientX,y:event.clientY,tx:state.view.tx,ty:state.view.ty};state.canvas.setPointerCapture(event.pointerId);});
  on(state.canvas,'pointermove',event=>{if(state.dragging){const dx=event.clientX-state.dragStart.x,dy=event.clientY-state.dragStart.y;if(Math.hypot(dx,dy)>3)state.dragMoved=true;if(state.dragMoved){state.view.tx=state.dragStart.tx+dx;state.view.ty=state.dragStart.ty+dy;requestRender();}return;}const hit=hitNode(event);if(hit!==state.hoverId){updateHover(hit);state.canvas.style.cursor=hit?'pointer':'grab';requestRender();}});
  const finish=event=>{if(!state.dragging)return;const moved=state.dragMoved,down=state.pointerDownNode;state.dragging=false;state.dragMoved=false;state.dragStart=null;state.pointerDownNode=null;if(state.canvas.hasPointerCapture(event.pointerId))state.canvas.releasePointerCapture(event.pointerId);if(!moved){const hit=hitNode(event);state.dotnet?.invokeMethodAsync('OnQuestSelected',hit&&hit===down?hit:null);}scheduleViewportSave();requestRender();};
  on(state.canvas,'pointerup',finish);on(state.canvas,'pointercancel',finish);
  on(state.canvas,'dblclick',event=>{event.preventDefault();const hit=hitNode(event);if(hit)state.dotnet?.invokeMethodAsync('OnQuestDoubleClicked',hit);});
  on(state.canvas,'pointerleave',()=>{if(!state.dragging&&state.hoverId){updateHover(null);requestRender();}});
  on(state.canvas,'keydown',event=>{if(event.key==='Escape')state.dotnet?.invokeMethodAsync('OnQuestSelected',null);else if(event.key==='+'||event.key==='=')zoom(1.22);else if(event.key==='-')zoom(1/1.22);});
}
