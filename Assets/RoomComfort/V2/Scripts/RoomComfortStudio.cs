using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RoomComfortV2
{
    // The scene is self-contained; the original EZPZ scene is not changed.
    public class RoomComfortStudio : MonoBehaviour
    {
        [Header("Room dimensions in metres (not an automatic scan)")]
        public float roomWidth = 10, roomDepth = 8, roomHeight = 3.2f;
        [Header("Existing furniture, reused from RoomComfort")]
        public Transform bed, desk, chair;
        public Transform shell, ac, acBody, preview, player, cameraPivot;
        public Camera view;
        public CharacterController walker;
        public Material wallMaterial, floorMaterial, woodMaterial, whiteMaterial, accentMaterial, darkMaterial, previewMaterial;
        public Font labelFont;
        public TextMesh wallInstructions;
        public Transform doorHinge;
        public List<Renderer> zones = new List<Renderer>();
        public List<Transform> flowArrows = new List<Transform>();

        [Header("Current session (runtime)")]
        public int step = 1, wallIndex = -1, strength = 1, bodySize = 1;
        public bool power, heating, placed, dragging;
        public float wallOffset, mountingHeight = 2.35f;
        public string posture = "Standing";
        public string notice = "Choose a numbered wall to begin.";
        public Vector3 SeatEye => chair.position + new Vector3(0, 1.18f, 0);
        public Vector3 BedEye => bed.position + new Vector3(0, .88f, .7f);
        public float PreviewRadius => new [] { 2.0f, 2.8f, 3.6f }[strength];
        public Vector3 PreviewCentre => ac.position + ac.forward * .45f + Vector3.up * (heating ? .32f : -.38f);
        public bool HasSaved => saved != null;

        [Serializable] public class Option
        {
            public int wall, level, size; public float offset, height; public bool heat;
        }
        Option saved, editOrigin;
        bool screenPanel = true, roomEditor, doorOpen, skipClick;
        float yaw = 180, pitch, gravitySpeed, doorAngle;
        Vector3 standingPosition;
        float standingYaw, standingPitch;
        Vector2 scroll;
        string widthText, depthText, heightText;
        string currentPrompt = "";
        Texture2D panelTex, buttonTex, activeTex;
        GUIStyle bodyStyle, headingStyle, smallStyle, buttonStyle, activeStyle, badgeStyle;
        float uiScale = 1;
        const float PanelWidth = 316;
        public bool IsPlacing => step == 2;

        void Start()
        {
            widthText = roomWidth.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            depthText = roomDepth.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            heightText = roomHeight.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            Application.targetFrameRate = 60;
            screenPanel = true;
            Refresh();
        }

        public static void RemoveObject(GameObject g)
        {
            if (!g) return;
            g.SetActive(false);
            if (Application.isPlaying) Destroy(g); else DestroyImmediate(g);
        }

        public GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, Material material, bool solid = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name; g.transform.SetParent(parent, false);
            g.transform.localPosition = pos; g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = material;
            if (!solid) { var c = g.GetComponent<Collider>(); c.enabled = false; if (!Application.isPlaying) DestroyImmediate(c); }
            return g;
        }

        public TextMesh Label(string name, Transform parent, string text, Vector3 position, Quaternion rotation, float size, Color color)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            g.transform.localPosition = position; g.transform.localRotation = rotation;
            var t = g.AddComponent<TextMesh>(); t.text = text; t.font = labelFont;
            t.fontSize = 64; t.characterSize = size; t.anchor = TextAnchor.MiddleCenter; t.alignment = TextAlignment.Center; t.color = color;
            if (labelFont) t.GetComponent<Renderer>().sharedMaterial = labelFont.material;
            return t;
        }

        public Vector3 WallNormal(int index) => new [] { Vector3.back, Vector3.right, Vector3.left, Vector3.forward }[Mathf.Clamp(index, 0, 3)];
        public Vector3 WallCentre(int index) => new [] { new Vector3(0,0,roomDepth/2), new Vector3(-roomWidth/2,0,0), new Vector3(roomWidth/2,0,0), new Vector3(0,0,-roomDepth/2) }[Mathf.Clamp(index,0,3)];
        public float WallSpan(int index) => index == 0 || index == 3 ? roomWidth : roomDepth;
        public float BodyWidth => new [] { .85f, 1.1f, 1.4f }[bodySize];

        public void LayoutRoom()
        {
            if (!shell) { shell = new GameObject("Room envelope (metres)").transform; shell.SetParent(transform, false); }
            foreach (Transform c in new List<Transform>(Children(shell))) RemoveObject(c.gameObject);
            Box("Floor", shell, new Vector3(0,-.1f,0), new Vector3(roomWidth,.2f,roomDepth), floorMaterial);
            Box("Wall 1", shell, new Vector3(0,roomHeight/2,roomDepth/2+.1f), new Vector3(roomWidth+.4f,roomHeight,.2f), wallMaterial);
            Box("Wall 2", shell, new Vector3(-roomWidth/2-.1f,roomHeight/2,0), new Vector3(.2f,roomHeight,roomDepth), wallMaterial);
            Box("Wall 3", shell, new Vector3(roomWidth/2+.1f,roomHeight/2,0), new Vector3(.2f,roomHeight,roomDepth), wallMaterial);
            float doorX = -roomWidth/2+1.2f, doorW = 1.1f;
            float leftLen = doorX-doorW/2+roomWidth/2;
            float rightStart = doorX+doorW/2, rightLen = roomWidth/2-rightStart;
            Box("Wall 4 left jamb", shell, new Vector3(-roomWidth/2+leftLen/2,roomHeight/2,-roomDepth/2-.1f), new Vector3(leftLen,roomHeight,.2f), wallMaterial);
            Box("Wall 4 right", shell, new Vector3(rightStart+rightLen/2,roomHeight/2,-roomDepth/2-.1f), new Vector3(rightLen,roomHeight,.2f), wallMaterial);
            Box("Door lintel", shell, new Vector3(doorX,(roomHeight+2.2f)/2,-roomDepth/2-.1f), new Vector3(doorW,roomHeight-2.2f,.2f), wallMaterial);
            Box("Entry landing", shell,new Vector3(doorX,-.1f,-roomDepth/2-1),new Vector3(2,.2f,2),floorMaterial);
            Box("Landing back",shell,new Vector3(doorX,1,-roomDepth/2-2),new Vector3(2,2,.1f),wallMaterial);
            Box("Landing side L",shell,new Vector3(doorX-1,1,-roomDepth/2-1),new Vector3(.1f,2,2),wallMaterial);
            Box("Landing side R",shell,new Vector3(doorX+1,1,-roomDepth/2-1),new Vector3(.1f,2,2),wallMaterial);
            doorHinge = new GameObject("Door hinge").transform; doorHinge.SetParent(shell,false); doorHinge.localPosition=new Vector3(doorX-doorW/2,0,-roomDepth/2);
            var leaf=Box("Door - click to open",doorHinge,new Vector3(doorW/2,1.08f,0),new Vector3(doorW,2.16f,.08f),woodMaterial);
            leaf.AddComponent<RCActionTarget>().action="Door";
            Box("Handle",doorHinge,new Vector3(doorW-.13f,1,.08f),new Vector3(.07f,.06f,.1f),accentMaterial,false);
            doorOpen=false;doorAngle=0;
            for(int i=0;i<4;i++)
            {
                var n=WallNormal(i); var c=WallCentre(i)+n*.04f;
                Label("Wall number "+(i+1),shell,(i+1).ToString(),c+Vector3.up*(roomHeight-.3f),Quaternion.LookRotation(-n),.18f,new Color(.15f,.35f,.36f));
                if(i<3) Box("Skirting "+i,shell,c+Vector3.up*.065f, i==0?new Vector3(roomWidth,.13f,.04f):new Vector3(.04f,.13f,roomDepth),whiteMaterial,false);
            }
            bed.position=new Vector3(-roomWidth/2+1.5f,0,roomDepth/2-1.6f);
            desk.position=new Vector3(roomWidth/2-.72f,0,roomDepth/2-1.8f);
            chair.position=new Vector3(roomWidth/2-1.75f,0,roomDepth/2-1.8f);
            // A readable, physical task board remains on the front wall.
            var board=Box("Control wall board",shell,new Vector3(.55f,1.65f,-roomDepth/2+.06f),new Vector3(3.5f,2.05f,.06f),darkMaterial,false);
            wallInstructions=Label("Current step on wall",shell,"",new Vector3(.55f,1.88f,-roomDepth/2+.11f),Quaternion.Euler(0,180,0),.036f,Color.white);
            for(int i=0;i<4;i++)
            {
                float x=-.65f+i*.8f;
                var b=Box("Wall "+(i+1)+" selector",shell,new Vector3(x,1.02f,-roomDepth/2+.15f),new Vector3(.68f,.28f,.08f),accentMaterial);
                var target=b.AddComponent<RCActionTarget>();target.action="Wall";target.index=i;
                Label("Wall "+(i+1)+" button text",shell,"Wall "+(i+1),new Vector3(x,1.02f,-roomDepth/2+.2f),Quaternion.Euler(0,180,0),.035f,Color.white);
            }
            Label("Bed prompt",bed,"BED\nClick nearby to lie down",new Vector3(0,.84f,-.8f),Quaternion.Euler(60,0,0),.026f,new Color(.12f,.24f,.26f));
            Label("Desk prompt",desk,"DESK / CHAIR\nClick the chair to sit",new Vector3(-.2f,.88f,0),Quaternion.Euler(65,-90,0),.022f,new Color(.12f,.24f,.26f));
            Physics.SyncTransforms();
            Refresh();
        }
        IEnumerable<Transform> Children(Transform p) { for(int i=0;i<p.childCount;i++) yield return p.GetChild(i); }

        public void SelectWall(int index)
        {
            if(posture!="Standing") StandUp();
            editOrigin=placed?Capture():null;
            wallIndex=Mathf.Clamp(index,0,3);wallOffset=0;mountingHeight=Mathf.Min(2.35f,roomHeight-.4f);
            if(wallIndex==3 && ForbiddenDoor(wallOffset)) wallOffset=.5f;
            step=2;power=false;dragging=false;screenPanel=true;roomEditor=false;
            notice="Hold and drag the AC. Release, then confirm. The tinted rectangle is the mounting area.";
            UpdateAC(); PlacementView(); Refresh();
        }
        public bool ForbiddenDoor(float offset)
        {
            if(wallIndex!=3) return false;
            float x=(WallCentre(wallIndex)+Vector3.Cross(Vector3.up,WallNormal(wallIndex))*offset).x;
            float doorX=-roomWidth/2+1.2f;
            return Mathf.Abs(x-doorX)<.55f+BodyWidth/2+.12f && mountingHeight-.2f<2.35f;
        }
        public bool ValidPlacement => wallIndex>=0 && !ForbiddenDoor(wallOffset);
        public void SetPlacement(float horizontal,float height)
        {
            wallOffset=Mathf.Clamp(horizontal,-WallSpan(wallIndex)/2+BodyWidth/2+.25f,WallSpan(wallIndex)/2-BodyWidth/2-.25f);
            mountingHeight=Mathf.Clamp(height,1.9f,roomHeight-.35f);
            UpdateAC();
        }
        public void UpdateAC()
        {
            if(wallIndex<0 || !ac) return;
            var n=WallNormal(wallIndex);ac.rotation=Quaternion.LookRotation(n);
            ac.position=WallCentre(wallIndex)+Vector3.Cross(Vector3.up,n)*wallOffset+Vector3.up*mountingHeight+n*.18f;
            acBody.localScale=new Vector3(BodyWidth,.38f,.28f);
            if(preview) {preview.position=PreviewCentre;preview.rotation=Quaternion.identity;}
        }
        void PlacementView()
        {
            walker.enabled=false;view.orthographic=true;
            view.orthographicSize=Mathf.Max(roomHeight*.72f,WallSpan(wallIndex)/(Mathf.Max(1,view.aspect))* .64f);
            var n=WallNormal(wallIndex);
            // Offset the wall towards the right of the screen, clear of the controls.
            Vector3 right=Vector3.Cross(Vector3.up,-n);
            view.transform.position=WallCentre(wallIndex)+Vector3.up*(roomHeight/2)+n*4.8f-right*(WallSpan(wallIndex)*.14f);
            view.transform.rotation=Quaternion.LookRotation(-n);
        }
        public void ConfirmPlacement()
        {
            if(!ValidPlacement){notice="Keep the unit clear of the door opening.";return;}
            placed=true;step=3;dragging=false;editOrigin=null;ReturnToPlayer();
            notice="Position confirmed. Turn AC On, then compare from the bed and desk.";Refresh();
        }
        public void EditPosition()
        {
            if(!placed)return;
            if(posture!="Standing")StandUp();
            editOrigin=Capture();step=2;power=false;screenPanel=true;PlacementView();Refresh();
        }
        public void CancelEdit()
        {
            if(editOrigin!=null){Apply(editOrigin);placed=true;step=3;editOrigin=null;}
            else {wallIndex=-1;placed=false;step=1;}
            dragging=false;ReturnToPlayer();Refresh();
        }
        public void ChooseAnotherWall()
        {
            power=false;wallIndex=-1;placed=false;step=1;dragging=false;editOrigin=null;ReturnToPlayer();Refresh();
        }
        void ReturnToPlayer()
        {
            view.orthographic=false; view.transform.SetParent(cameraPivot,false);view.transform.localPosition=Vector3.zero;view.transform.localRotation=Quaternion.identity;
            walker.enabled=posture=="Standing";
        }
        public void SetPower(bool on)
        {
            if(!placed || step==2)return;
            power=on;if(on)step=4;
            notice=on?"Preview on. Walk to the bed or desk; click the nearby furniture to change viewpoint.":"Preview off. Your AC position is unchanged.";
            Refresh();
        }
        public void SetMode(bool heat){heating=heat;UpdateAC();Refresh();}
        public void SetStrength(int level){strength=Mathf.Clamp(level,0,2);Refresh();}
        public void SetBodySize(int size){bodySize=Mathf.Clamp(size,0,2);if(wallIndex>=0)SetPlacement(wallOffset,mountingHeight);Refresh();}
        public Option Capture()=>new Option{wall=wallIndex,offset=wallOffset,height=mountingHeight,level=strength,heat=heating,size=bodySize};
        void Apply(Option o){wallIndex=o.wall;wallOffset=o.offset;mountingHeight=o.height;strength=o.level;heating=o.heat;bodySize=o.size;UpdateAC();}
        public void SaveOption(){if(!placed || IsPlacing)return;saved=Capture();notice="Option A saved for this session. Edit the position to compare another option.";Refresh();}
        public void RestoreOption(){if(saved==null)return;Apply(saved);placed=true;step=power?4:3;notice="Option A restored, including its mode and strength.";Refresh();}
        public bool IsInside(Vector3 point)=>power && Vector3.Distance(point,PreviewCentre)<=PreviewRadius;

        public void SitOrLie(bool lie)
        {
            if(IsPlacing)return;
            Vector3 near=lie?bed.position:chair.position;
            if(Vector3.Distance(new Vector3(player.position.x,0,player.position.z),near)>2.5f){notice="Walk closer to the "+(lie?"bed":"chair")+" first.";return;}
            if(posture=="Standing"){standingPosition=player.position;standingYaw=yaw;standingPitch=pitch;}
            posture=lie?"Lying on bed":"Sitting at desk";walker.enabled=false;
            player.position=lie?BedEye-Vector3.up*1.65f:SeatEye-Vector3.up*1.65f;
            yaw=lie?180:90;pitch=lie?-28:0;SetLook();screenPanel=true;
            notice="Hold right mouse to look around. Stand up to return to the clear spot you came from.";Refresh();
        }
        public void StandUp()
        {
            if(posture=="Standing")return;
            player.position=standingPosition;yaw=standingYaw;pitch=standingPitch;posture="Standing";gravitySpeed=0;
            walker.enabled=true;SetLook();notice="Standing. WASD to walk; hold right mouse to look.";Refresh();
        }
        public void Refresh()
        {
            if(ac)ac.gameObject.SetActive(wallIndex>=0);
            if(preview)preview.gameObject.SetActive(power && placed && step!=2);
            for(int i=0;i<zones.Count;i++)if(zones[i])
            {
                float r=PreviewRadius*new[]{.42f,.72f,1f}[i];zones[i].transform.localScale=Vector3.one*r*2;
                var props=new MaterialPropertyBlock();
                Color c=heating?new Color(1,.42f,.12f,.045f):new Color(.1f,.65f,.95f,.045f);
                props.SetColor("_Tint",c);props.SetVector("_RoomMin",new Vector4(-roomWidth/2,0,-roomDepth/2,0));props.SetVector("_RoomMax",new Vector4(roomWidth/2,roomHeight,roomDepth/2,0));
                zones[i].SetPropertyBlock(props);
            }
            if(wallInstructions)
            {
                string line=step==1?"STEP 1 / CHOOSE A WALL\nSelect wall 1, 2, 3 or 4 below.":step==2?"STEP 2 / PLACE THE AC\nDrag on the highlighted wall.\nConfirm when ready.":step==3?"STEP 3 / TRY THE PREVIEW\nTurn AC On. Choose mode and strength.":"STEP 4 / COMPARE YOUR SPOTS\nWalk to the bed or chair.\nClick nearby to lie down or sit.";
                wallInstructions.text="ROOM COMFORT\nFind a place for your AC\n\n"+line+"\n\nIllustrative zones, not calculated temperatures.\nTab opens / closes the controls.";
            }
        }

        public bool ResizeRoom(float width,float depth,float height)
        {
            if(width<6||width>14||depth<6||depth>14||height<2.6f||height>4||float.IsNaN(width+depth+height)||float.IsInfinity(width+depth+height)) {notice="Use width / depth 6-14 m and height 2.6-4 m.";return false;}
            if(posture!="Standing")StandUp();
            roomWidth=width;roomDepth=depth;roomHeight=height;saved=null;editOrigin=null;power=false;placed=false;wallIndex=-1;step=1;
            // Remove only generated labels before relayout, preserving original meshes.
            foreach(var t in new[]{bed,desk})foreach(var x in t.GetComponentsInChildren<TextMesh>())RemoveObject(x.gameObject);
            LayoutRoom();walker.enabled=false;player.position=Vector3.zero;walker.enabled=true;yaw=180;pitch=0;ReturnToPlayer();SetLook();
            roomEditor=false;notice="Room resized. Furniture sizes are unchanged. Choose a wall again; previous options were cleared.";return true;
        }

        void SetLook(){player.rotation=Quaternion.Euler(0,yaw,0);cameraPivot.localRotation=Quaternion.Euler(pitch,0,0);}
        bool OverPanel(Vector2 p)=>screenPanel && p.x< (PanelWidth+24)*uiScale;
        void Update()
        {
            var mouse=Mouse.current;var keyboard=Keyboard.current;if(mouse==null||keyboard==null)return;
            if(keyboard.tabKey.wasPressedThisFrame)screenPanel=!screenPanel;
            if(keyboard.escapeKey.wasPressedThisFrame){if(IsPlacing)CancelEdit();else if(posture!="Standing")StandUp();else screenPanel=!screenPanel;}
            Vector2 pointer=mouse.position.ReadValue();
            if(IsPlacing)
            {
                if(mouse.leftButton.wasPressedThisFrame && !OverPanel(pointer))
                {
                    if(Physics.Raycast(view.ScreenPointToRay(pointer),out var hit,30) && hit.transform.IsChildOf(ac))dragging=true;
                }
                if(dragging && mouse.leftButton.isPressed)
                {
                    var n=WallNormal(wallIndex);var plane=new Plane(n,WallCentre(wallIndex)+n*.18f);
                    var ray=view.ScreenPointToRay(pointer);
                    if(plane.Raycast(ray,out float d)){Vector3 p=ray.GetPoint(d)-WallCentre(wallIndex);SetPlacement(Vector3.Dot(p,Vector3.Cross(Vector3.up,n)),p.y);}
                }
                if(mouse.leftButton.wasReleasedThisFrame)dragging=false;
                currentPrompt=dragging?"Release to stop dragging":"Hold left mouse on the AC to drag";
            }
            else
            {
                if(mouse.rightButton.isPressed && !OverPanel(pointer))
                {
                    var delta=mouse.delta.ReadValue();yaw+=delta.x*.1f;pitch=Mathf.Clamp(pitch-delta.y*.1f,-75,75);SetLook();
                }
                if(posture=="Standing" && walker.enabled && !roomEditor)
                {
                    float x=(keyboard.dKey.isPressed?1:0)-(keyboard.aKey.isPressed?1:0),z=(keyboard.wKey.isPressed?1:0)-(keyboard.sKey.isPressed?1:0);
                    Vector3 movement=Vector3.ClampMagnitude(player.right*x+player.forward*z,1)*2.2f;
                    gravitySpeed=walker.isGrounded?-2:Mathf.Max(-20,gravitySpeed-18*Time.deltaTime);
                    walker.Move((movement+Vector3.up*gravitySpeed)*Time.deltaTime);
                    if(player.position.y<-2){walker.enabled=false;player.position=Vector3.zero;walker.enabled=true;}
                }
                currentPrompt="";
                if(!OverPanel(pointer) && Physics.Raycast(view.ScreenPointToRay(pointer),out var hit,20))
                {
                    var action=hit.transform.GetComponentInParent<RCActionTarget>();
                    if(action)
                    {
                        bool close=Vector3.Distance(view.transform.position,hit.point)<3;
                        currentPrompt=action.action=="Wall"?"Click to select Wall "+(action.index+1):close?"Click: "+action.action:"Walk closer to "+action.action.ToLowerInvariant();
                        if(mouse.leftButton.wasPressedThisFrame && (close||action.action=="Wall"))
                        {
                            if(action.action=="Wall")SelectWall(action.index);
                            else if(action.action=="Sit down")SitOrLie(false);
                            else if(action.action=="Lie down")SitOrLie(true);
                            else if(action.action=="Door")doorOpen=!doorOpen;
                        }
                    }
                }
            }
            if(doorHinge){doorAngle=Mathf.MoveTowards(doorAngle,doorOpen?-95:0,Time.deltaTime*100);doorHinge.localRotation=Quaternion.Euler(0,doorAngle,0);}
            if(preview && preview.gameObject.activeSelf)
            {
                for(int i=0;i<flowArrows.Count;i++)
                {
                    var a=flowArrows[i];float t=Mathf.Repeat(Time.time*.32f+i*.17f,1);
                    a.position=ac.position+ac.forward*(.35f+t*1.5f)+ac.right*Mathf.Sin(i*2.1f)*.45f+Vector3.up*(heating?t*.55f:-t*.7f);
                    a.rotation=Quaternion.LookRotation((ac.forward+Vector3.up*(heating?.8f:-.6f)).normalized);
                    a.gameObject.SetActive(a.position.y>.1f && a.position.y<roomHeight-.1f);
                }
            }
        }

        void OnDrawGizmos()
        {
            if(!IsPlacing || wallIndex<0)return;
            Gizmos.color=new Color(.15f,.8f,.65f,.4f);
            var n=WallNormal(wallIndex);Gizmos.matrix=Matrix4x4.TRS(WallCentre(wallIndex)+n*.05f+Vector3.up*(roomHeight+1.65f)/2,Quaternion.LookRotation(n),Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero,new Vector3(WallSpan(wallIndex)-.4f,roomHeight-1.65f,.02f));Gizmos.matrix=Matrix4x4.identity;
        }

        Texture2D Solid(Color c){var t=new Texture2D(1,1);t.SetPixel(0,0,c);t.Apply();return t;}
        void InitStyles()
        {
            if(bodyStyle!=null)return;
            panelTex=Solid(new Color(.055f,.105f,.13f,.97f));buttonTex=Solid(new Color(.14f,.23f,.27f));activeTex=Solid(new Color(.1f,.48f,.44f));
            bodyStyle=new GUIStyle(GUI.skin.label){fontSize=14,wordWrap=true,richText=true};bodyStyle.normal.textColor=new Color(.9f,.95f,.95f);
            headingStyle=new GUIStyle(bodyStyle){fontSize=23,fontStyle=FontStyle.Bold};
            smallStyle=new GUIStyle(bodyStyle){fontSize=12};smallStyle.normal.textColor=new Color(.64f,.77f,.8f);
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=14,wordWrap=true,fixedHeight=37,margin=new RectOffset(3,3,4,4)};
            buttonStyle.normal.background=buttonTex;buttonStyle.normal.textColor=Color.white;buttonStyle.hover.textColor=Color.white;
            buttonStyle.hover.background=activeTex;buttonStyle.active.background=activeTex;
            activeStyle=new GUIStyle(buttonStyle);activeStyle.normal.background=activeTex;
            badgeStyle=new GUIStyle(bodyStyle){fontStyle=FontStyle.Bold,fontSize=15};
        }
        bool Button(string text,bool selected=false)=>GUILayout.Button(text,selected?activeStyle:buttonStyle);
        void Text(string text,bool small=false)=>GUILayout.Label(text,small?smallStyle:bodyStyle);
        void Gap()=>GUILayout.Space(10);
        void OnGUI()
        {
            InitStyles();uiScale=Mathf.Clamp(Screen.height/820f,.72f,1.5f);GUI.matrix=Matrix4x4.Scale(new Vector3(uiScale,uiScale,1));
            float h=Screen.height/uiScale,w=Screen.width/uiScale;
            if(screenPanel)
            {
                GUI.DrawTexture(new Rect(12,12,PanelWidth,h-24),panelTex);
                GUILayout.BeginArea(new Rect(26,24,PanelWidth-28,h-48));
                scroll=GUILayout.BeginScrollView(scroll);
                GUILayout.Label("ROOM COMFORT",headingStyle);
                Text("Find a place for your AC",true);Gap();
                GUILayout.Label("STEP "+step+" / 4",badgeStyle);
                if(roomEditor) DrawRoomEditor();
                else if(step==1)
                {
                    Text("Choose a wall");Text("The room walls are numbered. Select one to start placing the AC.");Gap();
                    for(int i=0;i<4;i++)if(Button("Wall "+(i+1)))SelectWall(i);
                    Gap();Text("Your room: "+roomWidth.ToString("0.0")+" x "+roomDepth.ToString("0.0")+" x "+roomHeight.ToString("0.0")+" m");
                    Text("Volume: "+(roomWidth*roomDepth*roomHeight).ToString("0.0")+" m³",true);
                    if(Button("Set room dimensions"))roomEditor=true;
                }
                else if(step==2)
                {
                    Text("Place on Wall "+(wallIndex+1));Text("Hold left mouse on the AC and drag it along the wall. Release to stop.");Gap();
                    Text("Unit width (physical fit only)",true);GUILayout.BeginHorizontal();
                    for(int i=0;i<3;i++)if(Button(new[]{"0.85 m","1.10 m","1.40 m"}[i],bodySize==i))SetBodySize(i);GUILayout.EndHorizontal();
                    Text("Mounting height: "+mountingHeight.ToString("0.00")+" m",true);
                    Text("Fine adjustment (metres)",true);
                    float u=GUILayout.HorizontalSlider(wallOffset,-WallSpan(wallIndex)/2+BodyWidth/2+.25f,WallSpan(wallIndex)/2-BodyWidth/2-.25f);
                    float y=GUILayout.HorizontalSlider(mountingHeight,1.9f,roomHeight-.35f);
                    if(Mathf.Abs(u-wallOffset)>.001f||Mathf.Abs(y-mountingHeight)>.001f)SetPlacement(u,y);
                    Text(ValidPlacement?"Placement available":"Keep the AC clear of the door.",true);
                    GUI.enabled=ValidPlacement;if(Button("Confirm placement"))ConfirmPlacement();GUI.enabled=true;
                    if(Button("Reset position"))SetPlacement(0,Mathf.Min(2.35f,roomHeight-.4f));
                    if(Button("Choose another wall"))ChooseAnotherWall();
                    if(Button("Cancel edit"))CancelEdit();
                }
                else
                {
                    Text(step==3?"Try the preview":"Compare your usual spots");
                    Text("Wall "+(wallIndex+1)+"  /  "+(heating?"Heating":"Cooling")+"  /  "+new[]{"Low","Medium","High"}[strength]+"  /  "+(power?"ON":"OFF"),true);
                    GUILayout.BeginHorizontal();if(Button("AC On",power))SetPower(true);if(Button("AC Off",!power))SetPower(false);GUILayout.EndHorizontal();
                    GUILayout.BeginHorizontal();if(Button("Cooling",!heating))SetMode(false);if(Button("Heating",heating))SetMode(true);GUILayout.EndHorizontal();
                    Text("Preview strength",true);GUILayout.BeginHorizontal();for(int i=0;i<3;i++)if(Button(new[]{"Low","Medium","High"}[i],strength==i))SetStrength(i);GUILayout.EndHorizontal();
                    if(heating)Text("Upward markers illustrate rising warm air, not a calculated airflow.",true);
                    if(Button("Edit position"))EditPosition();
                    if(Button("Choose another wall"))ChooseAnotherWall();
                    Gap();Text(posture);Text(power?(IsInside(view.transform.position)?"Your viewpoint: inside the preview zone":"Your viewpoint: outside the preview zone"):"Turn AC On to compare coverage.",true);
                    if(posture!="Standing"){if(Button("Stand up"))StandUp();}
                    else
                    {
                        bool nearBed=Vector3.Distance(new Vector3(player.position.x,0,player.position.z),bed.position)<2.5f;
                        bool nearChair=Vector3.Distance(new Vector3(player.position.x,0,player.position.z),chair.position)<2.5f;
                        if(nearBed && Button("Lie on bed"))SitOrLie(true);
                        if(nearChair && Button("Sit at desk"))SitOrLie(false);
                        if(!nearBed&&!nearChair)Text("Walk closer to the bed or chair to sit / lie down.",true);
                    }
                    Gap();Text("Compare positions",true);if(Button("Save as Option A"))SaveOption();
                    if(saved!=null){Text("A: Wall "+(saved.wall+1)+" / "+(saved.heat?"Heat":"Cool")+" / "+new[]{"Low","Medium","High"}[saved.level],true);if(Button("Restore Option A"))RestoreOption();}
                    Text("Keep the same mode and strength for a fair position comparison.",true);
                }
                Gap();Text(notice);Gap();
                Text("WASD: walk\nHold right mouse: look around\nTab: show / hide controls\nEsc: cancel edit / stand up",true);
                Gap();Text("Illustrative preview only. No computed temperatures, energy use or appliance sizing recommendation.",true);
                GUILayout.EndScrollView();GUILayout.EndArea();
            }
            else {GUI.DrawTexture(new Rect(12,12,242,36),panelTex);GUI.Label(new Rect(23,19,230,25),"ROOM COMFORT   /   Tab: controls",smallStyle);}
            if(!string.IsNullOrEmpty(currentPrompt)){GUI.DrawTexture(new Rect(w/2-200,h-50,400,34),panelTex);GUI.Label(new Rect(w/2-190,h-44,380,28),currentPrompt,bodyStyle);}
            if(posture!="Standing" && !screenPanel && GUI.Button(new Rect(w-155,15,140,40),"Stand up",buttonStyle))StandUp();
            GUI.matrix=Matrix4x4.identity;
        }
        void DrawRoomEditor()
        {
            Text("Room dimensions");Text("Rectangular room, metres. Furniture stays the same size.",true);
            Text("Width (6-14 m)");widthText=GUILayout.TextField(widthText,12);
            Text("Depth (6-14 m)");depthText=GUILayout.TextField(depthText,12);
            Text("Height (2.6-4 m)");heightText=GUILayout.TextField(heightText,12);
            Text("Applying dimensions clears the current placement and saved comparison.",true);
            if(Button("Apply room dimensions"))
            {
                var c=System.Globalization.CultureInfo.InvariantCulture;
                if(float.TryParse(widthText,System.Globalization.NumberStyles.Float,c,out float a)&&float.TryParse(depthText,System.Globalization.NumberStyles.Float,c,out float b)&&float.TryParse(heightText,System.Globalization.NumberStyles.Float,c,out float d))ResizeRoom(a,b,d);
                else notice="Enter numbers using a decimal point, for example 3.2.";
            }
            if(Button("Back"))roomEditor=false;
            Text("This is manual entry, not a room scan. Volume alone does not determine the correct AC capacity.",true);
        }
        void OnDestroy(){foreach(var t in new[]{panelTex,buttonTex,activeTex})if(t)Destroy(t);}
    }
}
