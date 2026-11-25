import json, uuid
U=["Alex","Sam","Robin","Kim"]
def gid(): return str(uuid.uuid4())
def up(name,status,w,t,flags=(),last=None,act="2026-11-20T20:00:00Z"):
    return {"userId":gid(),"userName":name,"status":status,"watched":w,"partial":0,"total":t,"lastActivity":None if status=="NotStarted" else act,"finishedAt":act if status=="Finished" else None,"lastEpisode":last,"flags":list(flags)}
def ns(n,t): return up(n,"NotStarted",0,t)
series=[]
def S(name,status,eps,users,seasons,elig,reason,kept=False):
    series.append({"seriesId":gid(),"name":name,"status":status,"kept":kept,"tvdbId":"1","episodeCount":eps,"users":users,"seasons":seasons,"eligible":elig,"reason":reason})
def Se(n,eps,users,elig,reason,kept=False):
    return {"seasonId":gid(),"name":f"Season {n}","number":n,"episodeCount":eps,"users":users,"eligible":elig,"reason":reason,"kept":kept}
S("Harbor Lights","Ended",30,[up("Alex","Finished",30,30,last="S03E10"),up("Sam","Finished",30,30,last="S03E10"),ns("Robin",30),ns("Kim",30)],
  [Se(i,10,[up("Alex","Finished",10,10),up("Sam","Finished",10,10),ns("Robin",10),ns("Kim",10)],True,"Finished by Alex, Sam") for i in (1,2,3)],True,"Finished by Alex, Sam")
S("Iron Coast","Ended",30,[up("Alex","Finished",30,30),up("Kim","InProgress",14,30,["Stalled"],"S02E04","2026-09-02T21:00:00Z"),ns("Sam",30),ns("Robin",30)],
  [Se(1,10,[up("Alex","Finished",10,10),up("Kim","Finished",10,10),ns("Sam",10),ns("Robin",10)],True,"Finished by Alex, Kim"),
   Se(2,10,[up("Alex","Finished",10,10),up("Kim","InProgress",4,10,["Stalled"],"S02E04","2026-09-02T21:00:00Z"),ns("Sam",10),ns("Robin",10)],False,"Waiting on Kim (4/10, stalled)"),
   Se(3,10,[up("Alex","Finished",10,10),ns("Kim",10),ns("Sam",10),ns("Robin",10)],True,"Finished by Alex")],False,"Waiting on Kim (14/30, stalled)")
S("Midnight Garden","Ended",12,[up("Sam","InProgress",7,12,last="S01E08"),up("Robin","InProgress",2,12,["Sampled","MidEpisode"],"S01E03","2026-10-01T19:00:00Z"),ns("Alex",12),ns("Kim",12)],
  [Se(1,12,[up("Sam","InProgress",7,12),up("Robin","InProgress",2,12,["Sampled","MidEpisode"]),ns("Alex",12),ns("Kim",12)],False,"Waiting on Sam (7/12), Robin (2/12, sampled, mid-episode)")],False,"Waiting on Sam (7/12), Robin (2/12, sampled, mid-episode)")
S("Paper Moons","Ended",16,[up("Alex","Finished",16,16),up("Robin","Finished",16,16),ns("Sam",16),ns("Kim",16)],
  [Se(i,8,[up("Alex","Finished",8,8),up("Robin","Finished",8,8),ns("Sam",8),ns("Kim",8)],False,"Everyone finished, but it's marked keep") for i in (1,2)],False,"Everyone finished, but it's marked keep",kept=True)
S("Quiet Streets","Ended",8,[ns(u,8) for u in U],[Se(1,8,[ns(u,8) for u in U],False,"Nobody has started it")],False,"Nobody has started it")
S("Starfall Academy","Continuing",36,[up("Alex","InProgress",30,36,last="S03E06",act="2026-11-24T21:00:00Z"),up("Kim","InProgress",26,36,last="S03E02",act="2026-11-23T21:00:00Z"),ns("Sam",36),ns("Robin",36)],
  [Se(1,12,[up("Alex","Finished",12,12),up("Kim","Finished",12,12),ns("Sam",12),ns("Robin",12)],True,"Finished by Alex, Kim"),
   Se(2,12,[up("Alex","Finished",12,12),up("Kim","Finished",12,12),ns("Sam",12),ns("Robin",12)],False,"Everyone finished; grace period until 2026-11-28",kept=True),
   Se(3,12,[up("Alex","InProgress",6,12,act="2026-11-24T21:00:00Z"),up("Kim","InProgress",2,12,act="2026-11-23T21:00:00Z"),ns("Sam",12),ns("Robin",12)],False,"Waiting on Alex (6/12), Kim (2/12)")],False,"Show status is Continuing")
# fix season 2 of Starfall: kept instead of grace
series[-1]["seasons"][1]["reason"]="Everyone finished, but it's marked keep"
S("The Long Road","Continuing",20,[up("Sam","Finished",20,20),up("Robin","Finished",20,20),ns("Alex",20),ns("Kim",20)],
  [Se(1,10,[up("Sam","Finished",10,10),up("Robin","Finished",10,10),ns("Alex",10),ns("Kim",10)],True,"Finished by Sam, Robin"),
   Se(2,10,[up("Sam","Finished",10,10),up("Robin","Finished",10,10),ns("Alex",10),ns("Kim",10)],False,"Everyone finished, but: Newest season of a show that's still continuing")],False,"Everyone finished, but: Show status is Continuing")
rep={"generatedAt":"2026-11-25T23:00:00Z","trackedUsers":U,"series":series}
G=1073741824
preview={"dryRun":True,"totalBytes":int(61.4*G),"error":None,"actions":[
 {"action":"WouldDelete","kind":"Series","seriesName":"Harbor Lights","seasonName":None,"detail":"Finished by Alex, Sam","bytes":int(38.2*G)},
 {"action":"WouldDelete","kind":"Season","seriesName":"Iron Coast","seasonName":"Season 1","detail":"Finished by Alex, Kim","bytes":int(7.9*G)},
 {"action":"WouldDelete","kind":"Season","seriesName":"Starfall Academy","seasonName":"Season 1","detail":"Finished by Alex, Kim","bytes":int(9.1*G)},
 {"action":"WouldDelete","kind":"Season","seriesName":"The Long Road","seasonName":"Season 1","detail":"Finished by Sam, Robin","bytes":int(6.2*G)},
 {"action":"Skipped","kind":"Season","seriesName":"Iron Coast","seasonName":"Season 3","detail":"Per-run limit of 50 GB reached","bytes":int(8.4*G)}]}
preview["totalBytes"]=sum(a["bytes"] for a in preview["actions"] if a["action"]=="WouldDelete")
hist=[
 {"date":"2026-11-24T03:00:12Z","action":"Deleted","kind":"Season","seriesName":"Northbound","seasonName":"Season 2","detail":"Finished by Alex, Sam · season 2 unmonitored in Sonarr","bytes":int(11.3*G)},
 {"date":"2026-11-24T03:00:09Z","action":"Deleted","kind":"Series","seriesName":"Glass Harbor","seasonName":None,"detail":"Finished by Robin · existing seasons unmonitored in Sonarr (show still monitored for new seasons)","bytes":int(24.8*G)},
 {"date":"2026-11-23T03:00:05Z","action":"WouldDelete","kind":"Season","seriesName":"Northbound","seasonName":"Season 2","detail":"Finished by Alex, Sam","bytes":int(11.3*G)},
 {"date":"2026-11-22T03:00:04Z","action":"Failed","kind":"Season","seriesName":"Cold Front","seasonName":"Season 1","detail":"Sonarr: Connection refused (192.168.1.50:8989)","bytes":int(5.6*G)},
]
users=[{"Id":gid(),"Name":n} for n in U+["Guest"]]
json.dump({"report":rep,"preview":preview,"history":hist,"users":users},open('/tmp/demo.json','w'))
print(len(json.dumps({"report":rep,"preview":preview,"history":hist,"users":users})))
