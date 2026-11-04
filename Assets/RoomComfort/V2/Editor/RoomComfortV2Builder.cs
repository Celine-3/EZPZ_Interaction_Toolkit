using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace RoomComfortV2.Editor
{
    [InitializeOnLoad]
    public static class RoomComfortV2Builder
    {
        const string Folder="Assets/RoomComfort/V2/";
        public const string ScenePath=Folder+"Scenes/RoomComfort_V2.unity";
        const string Command="Library/RoomComfortV2.command";
        static double nextPoll;
        static RoomComfortV2Builder(){EditorApplication.update+=Poll;}
        static void Poll()
        {
            if(EditorApplication.timeSinceStartup<nextPoll||EditorApplication.isCompiling||EditorApplication.isUpdating)return;
            nextPoll=EditorApplication.timeSinceStartup+1;
            if(!File.Exists(Command))return;
            string cmd=File.ReadAllText(Command).Trim();File.Delete(Command);
            try
            {
                if(cmd=="create")Create();
                else if(cmd=="open")EditorSceneManager.OpenScene(ScenePath);
                else if(cmd=="play")EditorApplication.isPlaying=true;
                else if(cmd=="stop")EditorApplication.isPlaying=false;
                else if(cmd=="test")RunChecks();
                else if(cmd=="capture")ScreenCapture.CaptureScreenshot("Library/RoomComfortV2-runtime.png");
                else if(cmd=="build")BuildWeb();
                else if(cmd=="status")WriteStatus();
                else if(cmd=="preview-bed") {var s=Find();s.SelectWall(0);s.SetPlacement(2,2.35f);s.ConfirmPlacement();s.SetPower(true);s.walker.enabled=false;s.player.position=s.bed.position+new Vector3(0,0,-1.8f);s.walker.enabled=true;s.SitOrLie(true);}
                File.WriteAllText("Library/RoomComfortV2-command-result.txt",DateTime.Now+" OK "+cmd);
            }
            catch(Exception e){Debug.LogException(e);File.WriteAllText("Library/RoomComfortV2-command-result.txt",e.ToString());}
        }
        static RoomComfortStudio Find()=>UnityEngine.Object.FindFirstObjectByType<RoomComfortStudio>();
        static Material Mat(string name,Color color,string shader="Universal Render Pipeline/Lit")
        {
            string path=Folder+"Materials/"+name+".mat";
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find(shader));AssetDatabase.CreateAsset(m,path);}
            m.color=color;return m;
        }
        static IEnumerable<Transform> All(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true));
        static Transform CopyFurniture(RoomComfortStudio studio,Scene scene,string name,string[] pieces,Vector3 pivot)
        {
            var root=new GameObject(name).transform;root.SetParent(studio.transform,false);
            foreach(string piece in pieces)
            {
                var source=All(scene).FirstOrDefault(t=>t.name==piece && !t.IsChildOf(studio.transform));
                if(!source)throw new Exception("Missing existing furniture: "+piece);
                var copy=UnityEngine.Object.Instantiate(source.gameObject,root);copy.name=piece;
                copy.transform.localPosition=source.position-pivot;copy.transform.localRotation=source.rotation;copy.transform.localScale=source.lossyScale;copy.SetActive(true);
                foreach(var r in copy.GetComponentsInChildren<Renderer>())r.sharedMaterial=piece.Contains("mattress")?studio.whiteMaterial:piece.Contains("Seat")?studio.accentMaterial:studio.woodMaterial;
                foreach(var c in copy.GetComponentsInChildren<Collider>())c.enabled=true;
            }
            return root;
        }
        [MenuItem("RoomComfort/Create upgraded V2 scene")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)throw new Exception("Exit Play mode before creating the scene.");
            if(File.Exists(ScenePath)){Debug.Log("V2 already exists. Use RoomComfort/Open V2 scene.");return;}
            var original=SceneManager.GetActiveScene();
            if(original.path!="Assets/RoomComfort/scenes/RoomComfort.unity")throw new Exception("Open the original RoomComfort scene before creating V2.");
            // Save a COPY of the in-memory scene. Even unsaved edits remain available in the original.
            EditorSceneManager.SaveScene(original,ScenePath,true);
            var scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);SceneManager.SetActiveScene(scene);
            var originals=scene.GetRootGameObjects();
            var reference=new GameObject("Previous layout - preserved (inactive)");
            foreach(var g in originals)g.transform.SetParent(reference.transform,true);
            reference.SetActive(false);
            var studio=new GameObject("RoomComfort V2 - interactive room").AddComponent<RoomComfortStudio>();
            studio.wallMaterial=Mat("Warm plaster",new Color(.82f,.86f,.83f));
            studio.floorMaterial=Mat("Light floor",new Color(.48f,.43f,.35f));
            studio.woodMaterial=Mat("Oak furniture",new Color(.42f,.27f,.15f));
            studio.whiteMaterial=Mat("Linen and AC",new Color(.9f,.92f,.9f));
            studio.accentMaterial=Mat("Teal controls",new Color(.12f,.48f,.45f),"Universal Render Pipeline/Unlit");
            studio.darkMaterial=Mat("Deep teal board",new Color(.035f,.09f,.12f),"Universal Render Pipeline/Unlit");
            studio.labelFont=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            studio.previewMaterial=Mat("Illustrative coverage",new Color(.1f,.65f,.95f,.045f),"RoomComfort/CoveragePreview");
            studio.bed=CopyFurniture(studio,scene,"Bed",new[]{"bed base","bed mattress","bed headboard"},new Vector3(-2,0,4));
            studio.desk=CopyFurniture(studio,scene,"Desk",new[]{"Desk top","Desk leg 1","Desk leg 2","Desk leg 3","Desk leg 4"},new Vector3(5.8f,0,1.5f));
            studio.chair=CopyFurniture(studio,scene,"Chair",new[]{"Chair Seat","Chair Back","Chair Base"},new Vector3(4.8f,0,1.5f));
            studio.bed.gameObject.AddComponent<RCActionTarget>().action="Lie down";
            studio.chair.gameObject.AddComponent<RCActionTarget>().action="Sit down";
            // Keep this prototype's input separate from the legacy prefab and its grab controller.
            studio.player=new GameObject("V2 Player - WASD and right mouse").transform;studio.player.SetParent(studio.transform,false);
            studio.walker=studio.player.gameObject.AddComponent<CharacterController>();studio.walker.height=1.75f;studio.walker.center=Vector3.up*.9f;studio.walker.radius=.25f;studio.walker.stepOffset=.22f;
            studio.cameraPivot=new GameObject("Eye height").transform;studio.cameraPivot.SetParent(studio.player,false);studio.cameraPivot.localPosition=Vector3.up*1.65f;
            var cg=new GameObject("V2 Main Camera");cg.tag="MainCamera";cg.transform.SetParent(studio.cameraPivot,false);
            studio.view=cg.AddComponent<Camera>();studio.view.nearClipPlane=.05f;studio.view.farClipPlane=80;studio.view.fieldOfView=65;
            studio.view.backgroundColor=new Color(.68f,.79f,.84f);studio.view.clearFlags=CameraClearFlags.SolidColor;
            cg.AddComponent<AudioListener>();var urp=cg.AddComponent<UniversalAdditionalCameraData>();urp.renderPostProcessing=false;
            studio.player.rotation=Quaternion.Euler(0,180,0);
            studio.ac=new GameObject("AC - movable assembly").transform;studio.ac.SetParent(studio.transform,false);
            studio.acBody=studio.Box("AC body - drag me",studio.ac,Vector3.zero,new Vector3(1.1f,.38f,.28f),studio.whiteMaterial).transform;
            studio.Box("Outlet",studio.ac,new Vector3(0,-.11f,.146f),new Vector3(.72f,.065f,.014f),studio.darkMaterial,false);
            studio.Box("Status light",studio.ac,new Vector3(.3f,.09f,.15f),new Vector3(.04f,.025f,.01f),studio.accentMaterial,false);
            studio.preview=new GameObject("Coverage preview - no colliders").transform;studio.preview.SetParent(studio.ac,false);
            for(int i=0;i<3;i++)
            {
                var sphere=GameObject.CreatePrimitive(PrimitiveType.Sphere);sphere.name=new[]{"Inner zone","Middle zone","Outer zone"}[i];sphere.transform.SetParent(studio.preview,false);
                UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());var r=sphere.GetComponent<Renderer>();r.sharedMaterial=studio.previewMaterial;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;studio.zones.Add(r);
            }
            for(int i=0;i<6;i++)
            {
                var a=new GameObject("Airflow marker "+i).transform;a.SetParent(studio.preview,false);
                studio.Box("stem",a,Vector3.zero,new Vector3(.018f,.018f,.18f),studio.accentMaterial,false);
                var l=studio.Box("arrow L",a,new Vector3(-.035f,0,.055f),new Vector3(.015f,.015f,.1f),studio.accentMaterial,false);l.transform.localRotation=Quaternion.Euler(0,-45,0);
                var r=studio.Box("arrow R",a,new Vector3(.035f,0,.055f),new Vector3(.015f,.015f,.1f),studio.accentMaterial,false);r.transform.localRotation=Quaternion.Euler(0,45,0);
                studio.flowArrows.Add(a);
            }
            studio.LayoutRoom();
            var light=new GameObject("Soft daylight").AddComponent<Light>();light.type=LightType.Directional;light.intensity=1.3f;light.transform.rotation=Quaternion.Euler(50,-30,0);light.shadows=LightShadows.Soft;
            var fill=new GameObject("Room fill light").AddComponent<Light>();fill.type=LightType.Point;fill.intensity=2;fill.range=18;fill.transform.position=new Vector3(0,2.9f,0);fill.shadows=LightShadows.None;
            RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(.62f,.65f,.68f);
            studio.Refresh();EditorSceneManager.MarkSceneDirty(scene);EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();EditorSceneManager.CloseScene(original,false);
            Selection.activeGameObject=studio.gameObject;
            if(SceneView.lastActiveSceneView)SceneView.lastActiveSceneView.LookAt(new Vector3(0,1,0),Quaternion.Euler(30,180,0),12);
            File.WriteAllText("Library/RoomComfortV2-created.txt",ScenePath);
            Debug.Log("RoomComfort V2 created. Original scene and build scene list were preserved.");
        }
        [MenuItem("RoomComfort/Open V2 scene")]
        public static void Open(){if(EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())EditorSceneManager.OpenScene(ScenePath);}

        static void WriteStatus()
        {
            var s=Find();
            File.WriteAllText("Library/RoomComfortV2-status.txt",s?JsonUtility.ToJson(new Status{step=s.step,wall=s.wallIndex,power=s.power,position=s.ac.position,posture=s.posture,errors=""},true):"No active V2");
        }
        [Serializable] class Status{public int step,wall;public bool power;public Vector3 position;public string posture,errors;}

        [MenuItem("RoomComfort/Run V2 interaction checks (Play mode)")]
        public static void RunChecks()
        {
            if(!EditorApplication.isPlaying)throw new Exception("Enter Play mode to run checks.");
            var s=Find();var checks=new List<string>();
            Action<bool,string> check=(ok,label)=>{if(!ok)throw new Exception("FAIL: "+label);checks.Add("PASS: "+label);};
            check(s!=null,"V2 controller exists");
            for(int i=0;i<4;i++)
            {
                s.SelectWall(i);check(s.ac.gameObject.activeSelf&&!s.power&&s.IsPlacing,"Wall "+(i+1)+" enters placement with preview off");
                s.SetPlacement(999,999);check(s.mountingHeight<=s.roomHeight-.34f,"Height clamps to wall");
                check(Mathf.Abs(s.wallOffset)<=s.WallSpan(i)/2-s.BodyWidth/2-.24f,"Unit edges stay inside wall");
                s.SetPlacement(0,2.4f);s.ConfirmPlacement();check(s.placed&&!s.IsPlacing&&!s.view.orthographic,"Confirm restores walking camera");
                s.SetPower(true);check(s.preview.gameObject.activeInHierarchy,"Power on enables preview");
                s.SetPower(false);check(!s.preview.gameObject.activeInHierarchy&&s.ac.gameObject.activeInHierarchy,"Off hides preview only");
            }
            s.SelectWall(3);s.SetPlacement(-s.roomWidth/2+1.2f,1.9f);check(!s.ValidPlacement,"Door overlap rejects placement");s.ConfirmPlacement();check(s.IsPlacing,"Invalid placement cannot confirm");s.CancelEdit();
            s.SelectWall(0);s.SetPlacement(.6f,2.25f);s.ConfirmPlacement();s.SetMode(true);s.SetStrength(2);s.SetPower(true);s.SaveOption();var savedPos=s.ac.position;
            s.SelectWall(1);s.ConfirmPlacement();s.RestoreOption();check(s.wallIndex==0&&s.strength==2&&s.heating&&Vector3.Distance(s.ac.position,savedPos)<.01f,"Option A restores position, mode and strength");
            s.SetStrength(0);float small=s.PreviewRadius;s.SetStrength(2);check(s.PreviewRadius>small,"Strength changes preview radius");
            s.EditPosition();s.SetPlacement(-1,2);s.CancelEdit();check(Vector3.Distance(s.ac.position,savedPos)<.01f,"Cancel edit restores confirmed position");
            s.walker.enabled=false;s.player.position=s.chair.position+new Vector3(-.8f,0,0);s.walker.enabled=true;var old=s.player.position;s.SitOrLie(false);check(s.posture=="Sitting at desk"&&!s.walker.enabled,"Sit changes viewpoint and disables walking");s.StandUp();check(s.walker.enabled&&Vector3.Distance(old,s.player.position)<.01f,"Stand returns to previous safe position");
            s.walker.enabled=false;s.player.position=s.bed.position+new Vector3(0,0,-1.7f);s.walker.enabled=true;s.SitOrLie(true);check(s.posture=="Lying on bed","Lie down viewpoint");s.StandUp();
            check(s.preview.GetComponentsInChildren<Collider>(true).Length==0,"Preview has no colliders");
            var bedScale=s.bed.lossyScale;check(!s.ResizeRoom(float.NaN,8,3),"Reject NaN dimensions");check(!s.ResizeRoom(3,8,3),"Reject too-small room");check(s.ResizeRoom(6,6,2.6f),"Resize minimum room");check(s.bed.lossyScale==bedScale,"Furniture size remains unchanged");check(!s.HasSaved&&s.wallIndex==-1&&!s.power,"Resize clears placement and saved option");
            s.ResizeRoom(10,8,3.2f);
            File.WriteAllLines("Library/RoomComfortV2-test-results.txt",checks);
            Debug.Log("RoomComfort V2: "+checks.Count+" checks passed.");
        }
        [MenuItem("RoomComfort/Build V2 WebGL")]
        public static void BuildWeb()
        {
            if(EditorApplication.isPlaying)throw new Exception("Exit Play mode before building.");
            if(EditorUserBuildSettings.activeBuildTarget!=BuildTarget.WebGL)throw new Exception("Switch build target to Web first.");
            var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{ScenePath},locationPathName="RoomComfort_V2_Web",target=BuildTarget.WebGL,options=BuildOptions.None});
            File.WriteAllText("Library/RoomComfortV2-build-result.txt",result.summary.result+" | errors="+result.summary.totalErrors+" | bytes="+result.summary.totalSize);
            if(result.summary.result!=UnityEditor.Build.Reporting.BuildResult.Succeeded)throw new Exception("V2 Web build did not succeed. See Console.");
        }
    }
}
