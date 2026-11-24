using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using StarterAssets;

// Attach to an always-active FlowManager, NOT to a step panel or the AC.
// This component is for the user's existing, manually arranged EZPZ scene.
public class RoomComfortManualFlow : MonoBehaviour
{
    [Header("Hint text ONLY. Never assign a parent containing buttons to these four slots.")]
    public GameObject step1, step2, step3Instructions, step4;
    [Header("All permanent buttons: wall choices, power, strength, edit and comparison")]
    public GameObject previewControls;
    public Transform acRoot, coverage;
    public Transform wall1, wall2, wall3, wall4;
    public Transform seatView, seatExit, bedView, bedExit;
    public Transform player;
    public Camera playerCamera;
    public MonoBehaviour movementController;
    public MonoBehaviour raycastInteractor;
    public GameObject seatPrompt, bedPrompt;
    [InspectorName("坐下／躺下操作提示")]
    [TextArea(3, 6)]
    public string restingInstructions = "Hold RIGHT mouse to look around.\nRelease to LEFT-click controls. Stand up / Esc to leave.";
    [Header("屏幕提示与按钮")]
    [InspectorName("界面大小") , Range(.8f, 1.5f)]
    public float overlaySize = 1f;
    [InspectorName("提示字号"), Range(16, 28)]
    public int overlayFontSize = 20;
    [InspectorName("调整位置操作提示"), TextArea(2, 5)]
    public string placementInstructions = "Hold LEFT mouse on the AC and drag it along the wall.\nRelease, then click Save position. Cancel / Esc returns without saving.";
    public float nearDistance = 2.2f;
    public float minimumMountHeight = 1.9f;
    public float bodyWidth = 1.3f, bodyHeight = .4f, wallStandOff = .2f;
    [Tooltip("Box colliders defining forbidden installation areas, e.g. a door/window. Optional.")]
    public BoxCollider[] blockedAreas;
    public float lowScale = .75f, mediumScale = 1f, highScale = 1.25f;

    public int currentStep = 1, currentWall = -1, level = 1;
    public bool power;
    [Header("Screen UI - enable only in the new scene")]
    public bool useScreenUI = false;
    public RoomComfortSceneUI sceneUI;
    [TextArea(2,5)] public string walkingInstructions = "Hold RIGHT mouse and drag to look around.\nWASD: walk. LEFT-click: controls. Tab: hide/show controls.";
    [TextArea(2,5)] public string overviewInstructions = "Overview: labels identify the AC, furniture, door and window. Choose a wall and turn AC On to compare. Click Return to view to leave.";
    public bool IsOverview => overview;
    public bool IsEditing => editing;
    public bool IsResting => resting;
    public bool IsPlacementValid => valid;
    public Transform ActiveRestPoint => !resting?null:restingExit==bedExit?bedView:seatView;
    public bool collapseControlsWhenResting=true;
    [Tooltip("Optional: ceiling or obstructing wall renderers to hide only in Overview. Colliders stay enabled.")]
    public Renderer[] overviewHiddenRenderers;
    readonly Dictionary<Renderer,bool> overviewRendererStates=new Dictionary<Renderer,bool>();
    bool panelBeforeRest;

    public bool PlannerBusy { get; private set; }
    bool panelBeforePlanner;
    Vector3 safeSpawn;

    public GameObject screenPanel;
    public TMP_Text screenHint;
    StarterAssetsInputs screenInput;
    bool savedCursorLocked, savedCursorLook, savedRayEnabled, screenReady;
    bool overview, projectionOrthographic;
    float projectionSize, projectionNear;
    RectTransform screenActionBar;
    Button panelToggle, overviewToggle, sitAction, lieAction, standAction, furnitureAction;
    readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    Button[] panelButtons;
    bool pointerOverUI;

    Vector3 originalCoverageScale, roomCentre;
    Vector3 cameraPosition, unitPosition;
    Quaternion cameraRotation, unitRotation;
    bool editing, dragging, confirmed, resting, valid;
    bool oldMovement, oldRay, oldCharacter;
    CursorLockMode oldCursor; bool oldVisible;
    readonly List<Behaviour> cameraBehaviours = new List<Behaviour>();
    CharacterController character;
    Transform restingExit;
    float lookYaw, lookPitch;
    Vector3 restEye;
    string statusValue="",statusAfter;
    float statusUntil;
    string status
    {
        get {if(statusUntil>0&&Time.unscaledTime>=statusUntil){statusValue=statusAfter;statusUntil=0;}return statusValue;}
        set {statusValue=value;statusUntil=0;}
    }
    void NotifyBriefly(string message,string after)
    {status=message;statusAfter=after;statusUntil=Time.unscaledTime+2;}

    bool saved;
    int savedWall, savedLevel;
    Vector3 savedPosition; Quaternion savedRotation;
    int rollbackWall;
    Vector3 rollbackPosition; Quaternion rollbackRotation;
    bool rollbackExists;
    bool editFromRest, rollbackPower;
    readonly bool[] remembered = new bool[4];
    readonly Vector3[] wallPositions = new Vector3[4];
    float ignoreClicksUntil;
    Bounds bounds;
    Vector3 inward, horizontal, planeCentre;
    float horizontalMin, horizontalMax, verticalMin, verticalMax;
    LineRenderer mountingOutline;
    Rect overlayPixels;
    Vector2 overlayScroll;
    GUIStyle overlayLabel, overlayButton;
    Texture2D overlayBackground;

    void Start()
    {
        if(!acRoot||!coverage||!wall1||!wall2||!wall3||!wall4||!player||!playerCamera||!movementController||!raycastInteractor)
        {Debug.LogError("RoomComfortManualFlow: assign AC, coverage, four walls, player, camera and both controller fields.",this);enabled=false;return;}
        if(useScreenUI && (!screenPanel || !screenHint || !screenPanel.GetComponentInParent<Canvas>()))
        { Debug.LogError("Screen UI: assign Screen Panel and Screen Hint before enabling Use Screen UI.",this); enabled=false;return; }
        if(useScreenUI&&!sceneUI){Debug.LogError("Run RoomComfort > Make UI Editable in Hierarchy before playing this scene.",this);enabled=false;return;}
        if(useScreenUI&&!sceneUI.ValidateBindings("actions.root","actions.controls","actions.overview","actions.sit","actions.lie","actions.stand","actions.furniture","hint.state","hint.feedback")){enabled=false;return;}
        status=M("flow.begin");
        safeSpawn=player.position;
        originalCoverageScale=coverage.localScale;
        roomCentre=(wall1.position+wall2.position+wall3.position+wall4.position)/4;
        character=player.GetComponent<CharacterController>();
        ShowHint(1);Set(previewControls,true);
        Set(seatPrompt,false);Set(bedPrompt,false);acRoot.gameObject.SetActive(false);coverage.gameObject.SetActive(false);
        if(useScreenUI) SetupScreenUI();
    }
    void Set(GameObject g,bool on){if(g)g.SetActive(on);}
    void ShowHint(int hint)
    {
        currentStep=hint;
        Set(step1,!useScreenUI&&hint==1);Set(step2,!useScreenUI&&hint==2);
        Set(step3Instructions,!useScreenUI&&hint==3);Set(step4,!useScreenUI&&hint==4);
    }
    public void ChooseWall1()=>ChooseWall(0);
    public void ChooseWall2()=>ChooseWall(1);
    public void ChooseWall3()=>ChooseWall(2);
    public void ChooseWall4()=>ChooseWall(3);

    void Freeze()
    {
        oldMovement=movementController.enabled;oldRay=raycastInteractor.enabled;
        movementController.enabled=false;raycastInteractor.enabled=false;
        if(character){oldCharacter=character.enabled;character.enabled=false;}
        cameraPosition=playerCamera.transform.position;cameraRotation=playerCamera.transform.rotation;
        projectionOrthographic=playerCamera.orthographic;projectionSize=playerCamera.orthographicSize;projectionNear=playerCamera.nearClipPlane;
        cameraBehaviours.Clear();
        foreach(var b in playerCamera.GetComponents<Behaviour>())
            if(b.enabled&&b.GetType().Name.Contains("CinemachineBrain")){cameraBehaviours.Add(b);b.enabled=false;}
        oldCursor=Cursor.lockState;oldVisible=Cursor.visible;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
    }
    void Unfreeze()
    {
        RestoreProjection();
        playerCamera.transform.SetPositionAndRotation(cameraPosition,cameraRotation);
        foreach(var b in cameraBehaviours)if(b)b.enabled=true;cameraBehaviours.Clear();
        if(character)character.enabled=oldCharacter;
        movementController.enabled=oldMovement;raycastInteractor.enabled=oldRay;
        Cursor.lockState=oldCursor;Cursor.visible=oldVisible;
    }
    bool ConfigureWall(int index)
    {
        Transform w=new[]{wall1,wall2,wall3,wall4}[index];var r=w.GetComponent<Renderer>();
        if(!r){status=M("flow.wallMesh");return false;}
        bounds=r.bounds;
        // Supports the four axis-aligned walls in the existing RoomComfort scene.
        bool alongX=bounds.size.x>bounds.size.z;
        inward=alongX?(roomCentre.z>w.position.z?Vector3.forward:Vector3.back):(roomCentre.x>w.position.x?Vector3.right:Vector3.left);
        horizontal=alongX?Vector3.right:Vector3.forward;
        planeCentre=bounds.center+Vector3.Scale(inward,bounds.extents)+inward*wallStandOff;
        horizontalMin=(alongX?bounds.min.x:bounds.min.z)+bodyWidth/2+.12f;
        horizontalMax=(alongX?bounds.max.x:bounds.max.z)-bodyWidth/2-.12f;
        verticalMin=Mathf.Max(minimumMountHeight,bounds.min.y+bodyHeight/2+.12f);
        verticalMax=bounds.max.y-bodyHeight/2-.12f;
        if(useScreenUI)
        {
            GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
            horizontalMin=Mathf.Max(horizontalMin,(alongX?xmin:zmin)+bodyWidth/2+.08f);
            horizontalMax=Mathf.Min(horizontalMax,(alongX?xmax:zmax)-bodyWidth/2-.08f);
        }
        return horizontalMax>horizontalMin&&verticalMax>verticalMin;
    }
    public void ChooseWall(int index)
    {
        if(PlannerBusy){status=M("flow.finishFurniture");return;}
        if(editing){status=M("flow.finishDrag");return;}
        if(index<0||index>3||!ConfigureWall(index))return;
        if(currentWall>=0&&confirmed){remembered[currentWall]=true;wallPositions[currentWall]=acRoot.position;}
        currentWall=index;power=false;coverage.gameObject.SetActive(false);acRoot.gameObject.SetActive(true);
        acRoot.rotation=Quaternion.LookRotation(inward);
        SetPosition(remembered[index]?wallPositions[index]:planeCentre+Vector3.up*(Mathf.Clamp(2.4f,verticalMin,verticalMax)-planeCentre.y));
        confirmed=true;ShowHint(2);Set(previewControls,true);
        var guide=GetComponent<RoomComfortVisualGuide>();if(guide)guide.HighlightWall(new[]{wall1,wall2,wall3,wall4}[index]);
        status=valid?M("flow.wallSelected","wall",(index+1).ToString()):M("flow.blocked");
    }
    void ShowOutline()
    {
        if(!mountingOutline)
        {
            var g=new GameObject("Mounting area outline");g.transform.SetParent(transform);mountingOutline=g.AddComponent<LineRenderer>();
            var guide=GetComponent<RoomComfortVisualGuide>();
            mountingOutline.sharedMaterial=guide&&guide.airflowMaterial?new Material(guide.airflowMaterial):new Material(Shader.Find("Universal Render Pipeline/Unlit"));mountingOutline.sharedMaterial.color=new Color(.1f,.75f,.6f);
            mountingOutline.startWidth=mountingOutline.endWidth=.025f;mountingOutline.useWorldSpace=true;mountingOutline.loop=true;mountingOutline.positionCount=4;
        }
        Vector3 a=planeCentre;a.y=verticalMin;a+=horizontal*(horizontalMin-Vector3.Dot(a,horizontal));
        mountingOutline.SetPositions(new[]{a,a+horizontal*(horizontalMax-horizontalMin),a+horizontal*(horizontalMax-horizontalMin)+Vector3.up*(verticalMax-verticalMin),a+Vector3.up*(verticalMax-verticalMin)});
        mountingOutline.gameObject.SetActive(true);
    }
    void SetPosition(Vector3 p)
    {
        float u=Mathf.Clamp(Vector3.Dot(p,horizontal),horizontalMin,horizontalMax);
        unitPosition=planeCentre+horizontal*(u-Vector3.Dot(planeCentre,horizontal));unitPosition.y=Mathf.Clamp(p.y,verticalMin,verticalMax);
        unitRotation=Quaternion.LookRotation(inward);acRoot.SetPositionAndRotation(unitPosition,unitRotation);
        valid=true;var candidate=new Bounds(unitPosition,Vector3.Scale(horizontal,Vector3.one*bodyWidth)+Vector3.up*bodyHeight+new Vector3(Mathf.Abs(inward.x),0,Mathf.Abs(inward.z))*.45f);
        if(blockedAreas!=null)foreach(var b in blockedAreas)if(b&&b.bounds.Intersects(candidate))valid=false;
    }
    public void ConfirmPlacement()
    {
        if(!editing||!valid)return;
        editing=false;dragging=false;confirmed=true;
        remembered[currentWall]=true;wallPositions[currentWall]=acRoot.position;
        if(mountingOutline)mountingOutline.gameObject.SetActive(false);
        if(!editFromRest)Unfreeze();else RestoreProjection();editFromRest=false;ShowHint(2);
        NotifyBriefly(M("flow.positionUpdated"),M("flow.afterPosition"));
        var guide=GetComponent<RoomComfortVisualGuide>();if(guide)guide.HighlightAC();
    }
    public void CancelPlacement()
    {
        if(!editing)return;
        editing=false;dragging=false;if(mountingOutline)mountingOutline.gameObject.SetActive(false);
        if(!editFromRest)Unfreeze();else RestoreProjection();editFromRest=false;
        if(rollbackExists){currentWall=rollbackWall;ConfigureWall(currentWall);SetPosition(rollbackPosition);confirmed=true;power=rollbackPower;coverage.gameObject.SetActive(power);ShowHint(power?(resting?4:3):2);}
        else{currentWall=-1;confirmed=false;acRoot.gameObject.SetActive(false);ShowHint(1);}
        status=M("flow.cancelPosition");
    }
    public void EditPosition()
    {
        if(PlannerBusy)return;
        if(!confirmed||editing){status=M("flow.chooseBeforeAdjust");return;}
        if(overview) ToggleOverview();
        if(!ConfigureWall(currentWall))return;
        rollbackExists=true;rollbackWall=currentWall;rollbackPosition=acRoot.position;rollbackRotation=acRoot.rotation;rollbackPower=power;
        editFromRest=resting;if(!resting)Freeze();
        editing=true;power=false;coverage.gameObject.SetActive(false);ignoreClicksUntil=Time.unscaledTime+.25f;
        SetPosition(acRoot.position);ShowOutline();status=M("flow.drag");
    }
    public void ResetPosition(){if(editing)SetPosition(planeCentre+Vector3.up*(Mathf.Clamp(2.4f,verticalMin,verticalMax)-planeCentre.y));}
    public void ChooseAnotherWall()
    {
        status=M("flow.anotherWall");
    }
    public void ACOn()
    {
        if(editing){status=M("flow.finishPosition");return;}
        if(!confirmed){status=M("flow.chooseBeforeOn");return;}
        if(!valid){status=M("flow.clearBeforeOn");return;}
        power=true;ApplyStrength();coverage.gameObject.SetActive(true);
        ShowHint(resting?4:3);status=resting?M("flow.previewRest"):M("flow.previewWalk");
    }
    public void ACOff(){power=false;coverage.gameObject.SetActive(false);if(confirmed&&!editing)ShowHint(2);status=M("flow.off");}
    public void Low()=>SetStrength(0);
    public void Medium()=>SetStrength(1);
    public void High()=>SetStrength(2);
    void SetStrength(int i){level=i;ApplyStrength();status=M("flow.strength","strength",StrengthText(),"result",M(power?"flow.previewUpdated":"flow.remainsOff"));}
    void ApplyStrength(){coverage.localScale=originalCoverageScale*new[]{lowScale,mediumScale,highScale}[level];}
    public void SaveOption(){if(!confirmed||editing)return;saved=true;savedWall=currentWall;savedPosition=acRoot.position;savedRotation=acRoot.rotation;savedLevel=level;status=M("flow.saveOld");}
    public void RestoreOption(){if(!saved||editing)return;currentWall=savedWall;ConfigureWall(currentWall);SetPosition(savedPosition);level=savedLevel;acRoot.gameObject.SetActive(true);confirmed=true;ApplyStrength();power=false;coverage.gameObject.SetActive(false);ShowHint(2);status=M("flow.restoreOld");}
    bool Near(Transform point)=>point&&Vector2.Distance(new Vector2(player.position.x,player.position.z),new Vector2(point.position.x,point.position.z))<nearDistance;
    public void SitDown()=>Rest(false);
    public void LieDown()=>Rest(true);
    void Rest(bool lie)
    {
        var eye=lie?bedView:seatView;var exit=lie?bedExit:seatExit;
        if(PlannerBusy||editing||overview||resting||!eye||!exit||!Near(eye))return;
        Freeze();resting=true;restingExit=exit;restEye=eye.position;lookYaw=eye.eulerAngles.y;lookPitch=Mathf.DeltaAngle(0,eye.eulerAngles.x);
        Set(seatPrompt,false);Set(bedPrompt,false);
        if(useScreenUI){panelBeforeRest=screenPanel.activeSelf;if(collapseControlsWhenResting)screenPanel.SetActive(false);}
        if(confirmed&&power)ShowHint(4);status=M("flow.rest");
    }
    public void StandUp()
    {
        if(!resting)return;
        if(overview) ToggleOverview();
        Vector3 destination=restingExit.position;
        if(useScreenUI&&!FindSafeExit(destination,out destination)){status=M("flow.noStand");return;}
        resting=false;Vector3 delta=destination-player.position;player.position=destination;cameraPosition+=delta;Unfreeze();
        if(useScreenUI)screenPanel.SetActive(panelBeforeRest);
    }
    void Update()
    {
        Set(seatPrompt,!useScreenUI&&!editing&&!resting&&Near(seatView));Set(bedPrompt,!useScreenUI&&!editing&&!resting&&Near(bedView));
        if(useScreenUI) { UpdateScreenInput(); RecoverOutsideRoom(); }
        var m=Mouse.current;var k=Keyboard.current;if(m==null||k==null)return;
        if(k.escapeKey.wasPressedThisFrame){if(editing)CancelPlacement();else if(overview)ToggleOverview();else if(resting)StandUp();}
        if(useScreenUI&&k.tabKey.wasPressedThisFrame)ToggleScreenPanel();
        if(editing)
        {
            var pointer=m.position.ReadValue();
            bool overToolbar=useScreenUI?pointerOverUI:overlayPixels.Contains(new Vector2(pointer.x,Screen.height-pointer.y));
            if(m.leftButton.wasPressedThisFrame&&!overToolbar&&Time.unscaledTime>ignoreClicksUntil&&Physics.Raycast(playerCamera.ScreenPointToRay(pointer),out var hit,100)&&hit.transform.IsChildOf(acRoot))dragging=true;
            if(dragging&&m.leftButton.isPressed)
            {
                var ray=playerCamera.ScreenPointToRay(pointer);var plane=new Plane(inward,planeCentre);
                if(plane.Raycast(ray,out float d))SetPosition(ray.GetPoint(d));
            }
            if(m.leftButton.wasReleasedThisFrame)dragging=false;
        }
        if(resting&&!overview&&m.rightButton.isPressed&&(!useScreenUI||!pointerOverUI)){var d=m.delta.ReadValue();lookYaw+=d.x*.1f;lookPitch=Mathf.Clamp(lookPitch-d.y*.1f,-75,75);}
    }
    void LateUpdate()
    {
        if(editing)
        {
            float span=horizontalMax-horizontalMin+bodyWidth;
            float distance=Mathf.Max(3,span/(2*Mathf.Tan(playerCamera.fieldOfView*Mathf.Deg2Rad*.5f)*playerCamera.aspect)*1.3f);
            if(useScreenUI)
            {
                GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
                float depth=Mathf.Abs(inward.x)>.5f?xmax-xmin:zmax-zmin;
                distance=Mathf.Min(distance,Mathf.Max(.6f,depth-wallStandOff-.45f));
                playerCamera.orthographic=true;playerCamera.nearClipPlane=.05f;
                float available=screenPanel.activeSelf?.68f:.92f;
                playerCamera.orthographicSize=Mathf.Max((verticalMax-verticalMin+bodyHeight)*.5f+.4f,
                    span/(2*Mathf.Max(.2f,playerCamera.aspect)*available)+.3f);
            }
            playerCamera.transform.position=planeCentre+inward*distance+Vector3.up*((verticalMin+verticalMax)/2-planeCentre.y);
            if(useScreenUI && screenPanel.activeSelf)
                playerCamera.transform.position+=Vector3.Cross(Vector3.up,-inward)*playerCamera.orthographicSize*playerCamera.aspect*.32f;
            playerCamera.transform.rotation=Quaternion.LookRotation(-inward);
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        }
        if(resting&&!editing&&!overview){playerCamera.transform.position=restEye;playerCamera.transform.rotation=Quaternion.Euler(lookPitch,lookYaw,0);Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        if(useScreenUI)
        {
            if(overview&&!editing)UpdateOverviewCamera();
            Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
            UpdateScreenUI();
        }
    }
    void OnGUI()
    {
        if(useScreenUI)return;
        // Reference-width scaling keeps text readable when WebGL renders at high resolution.
        float scale = Mathf.Max(.8f, Screen.width / 1100f) * overlaySize;
        float viewWidth = Screen.width / scale, viewHeight = Screen.height / scale;
        Matrix4x4 oldMatrix = GUI.matrix;
        Color oldColor = GUI.color;
        bool oldEnabled = GUI.enabled;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one * scale);
        GUI.color = Color.white;
        if (overlayLabel == null)
        {
            overlayLabel = new GUIStyle(GUI.skin.label) { wordWrap = true, alignment = TextAnchor.UpperLeft, padding = new RectOffset(0,0,0,0) };
            overlayLabel.normal.textColor = Color.white;
            overlayButton = new GUIStyle(GUI.skin.button) { wordWrap = true, alignment = TextAnchor.MiddleCenter };
            overlayBackground = new Texture2D(1,1);
            overlayBackground.SetPixel(0,0,new Color(.055f,.075f,.095f,.97f));
            overlayBackground.Apply();
        }
        overlayLabel.fontSize = overlayFontSize;
        overlayButton.fontSize = overlayFontSize;
        float width = Mathf.Min(viewWidth-24, 860);
        float contentWidth = Mathf.Max(80,width-48);
        string stateText = (currentWall<0?"No wall selected":"Wall "+(currentWall+1))+"  |  "+(power?"AC ON":"AC OFF")+"  |  "+new[]{"Low","Medium","High"}[level];
        string message = editing ? (valid ? placementInstructions : "This position overlaps a blocked area. Drag the AC to a clear area.") : resting ? restingInstructions : stateText+"\n"+status;
        float textHeight = overlayLabel.CalcHeight(new GUIContent(message),contentWidth);
        float buttonHeight = Mathf.Max(44,overlayFontSize*2.4f);
        int rows = editing ? 1 : resting ? 4 : 0;
        float stateHeight = resting&&!editing ? overlayLabel.CalcHeight(new GUIContent(stateText),contentWidth)+8 : 0;
        float contentHeight = textHeight+rows*(buttonHeight+8)+stateHeight;
        float height = Mathf.Min(contentHeight+24,viewHeight-24);
        Rect panel = new Rect(12,editing||resting ? 12 : viewHeight-height-12,width,height);
        overlayPixels = new Rect(panel.x*scale,panel.y*scale,panel.width*scale,panel.height*scale);
        GUI.DrawTexture(panel,overlayBackground);
        // A scrollable panel also accommodates longer user-edited instructions in small windows.
        overlayScroll = GUI.BeginScrollView(new Rect(panel.x+12,panel.y+12,width-24,height-24),overlayScroll,new Rect(0,0,contentWidth,contentHeight));
        GUI.Label(new Rect(0,0,contentWidth,textHeight),message,overlayLabel);
        float y = textHeight+8;
        if (editing)
        {
            GUI.enabled=valid;
            if(OverlayButton(0,3,y,contentWidth,buttonHeight,"Save position"))ConfirmPlacement();
            GUI.enabled=oldEnabled;
            if(OverlayButton(1,3,y,contentWidth,buttonHeight,"Reset position"))ResetPosition();
            if(OverlayButton(2,3,y,contentWidth,buttonHeight,"Cancel"))CancelPlacement();
        }
        else if (resting)
        {
            for(int i=0;i<4;i++)if(OverlayButton(i,4,y,contentWidth,buttonHeight,"Wall "+(i+1)))ChooseWall(i);
            y+=buttonHeight+8;
            if(OverlayButton(0,4,y,contentWidth,buttonHeight,power?"AC Off":"AC On")){if(power)ACOff();else ACOn();}
            if(OverlayButton(1,4,y,contentWidth,buttonHeight,"Low"))Low();
            if(OverlayButton(2,4,y,contentWidth,buttonHeight,"Medium"))Medium();
            if(OverlayButton(3,4,y,contentWidth,buttonHeight,"High"))High();
            y+=buttonHeight+8;
            if(OverlayButton(0,3,y,contentWidth,buttonHeight,"Adjust position"))EditPosition();
            if(OverlayButton(1,3,y,contentWidth,buttonHeight,"Save Option A"))SaveOption();
            GUI.enabled=saved;
            if(OverlayButton(2,3,y,contentWidth,buttonHeight,"Restore A"))RestoreOption();
            GUI.enabled=oldEnabled;y+=buttonHeight+8;
            if(OverlayButton(0,1,y,contentWidth,buttonHeight,"Stand up (Esc)"))StandUp();
            y+=buttonHeight+8;
            GUI.Label(new Rect(0,y,contentWidth,stateHeight),stateText,overlayLabel);
        }
        GUI.EndScrollView();
        GUI.enabled=oldEnabled;GUI.color=oldColor;GUI.matrix=oldMatrix;
    }
    bool OverlayButton(int column,int columns,float y,float width,float height,string text)
    {
        float buttonWidth=(width-8*(columns-1))/columns;
        return GUI.Button(new Rect(column*(buttonWidth+8),y,buttonWidth,height),text,overlayButton);
    }

    void RestoreProjection()
    {
        playerCamera.orthographic=projectionOrthographic;
        playerCamera.orthographicSize=projectionSize;playerCamera.nearClipPlane=projectionNear;
    }
    void GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax)
    {
        xmin=float.NegativeInfinity;xmax=float.PositiveInfinity;
        zmin=float.NegativeInfinity;zmax=float.PositiveInfinity;
        foreach(var wall in new[]{wall1,wall2,wall3,wall4})
        {
            var r=wall.GetComponent<Renderer>();if(!r)continue;var b=r.bounds;
            if(b.size.x>b.size.z)
            { if(b.center.z<roomCentre.z)zmin=Mathf.Max(zmin,b.max.z);else zmax=Mathf.Min(zmax,b.min.z); }
            else
            { if(b.center.x<roomCentre.x)xmin=Mathf.Max(xmin,b.max.x);else xmax=Mathf.Min(xmax,b.min.x); }
        }
        if(float.IsInfinity(xmin)||float.IsInfinity(xmax)||float.IsInfinity(zmin)||float.IsInfinity(zmax))
        { xmin=roomCentre.x-4;xmax=roomCentre.x+4;zmin=roomCentre.z-3;zmax=roomCentre.z+3; }
    }
    public void ToggleOverview()
    {
        if(!useScreenUI||editing||PlannerBusy)return;
        if(!overview){if(!resting)Freeze();overview=true;SetOverviewVisibility(true);}
        else {overview=false;SetOverviewVisibility(false);if(!resting)Unfreeze();else RestoreProjection();}
    }
    void SetOverviewVisibility(bool hide)
    {
        if(hide)
        {
            overviewRendererStates.Clear();
            if(overviewHiddenRenderers!=null)foreach(var renderer in overviewHiddenRenderers)
                if(renderer&&!overviewRendererStates.ContainsKey(renderer)){overviewRendererStates.Add(renderer,renderer.enabled);renderer.enabled=false;}
        }
        else {foreach(var item in overviewRendererStates)if(item.Key)item.Key.enabled=item.Value;overviewRendererStates.Clear();}
    }
    void UpdateOverviewCamera()
    {
        GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
        float top=0;foreach(var w in new[]{wall1,wall2,wall3,wall4})top=Mathf.Max(top,w.GetComponent<Renderer>().bounds.max.y);
        float available=(screenPanel.activeSelf||PlannerBusy)?.65f:.92f;
        playerCamera.orthographic=true;playerCamera.nearClipPlane=.05f;
        playerCamera.orthographicSize=Mathf.Max((zmax-zmin)*.5f+.8f,(xmax-xmin)/(2*Mathf.Max(.2f,playerCamera.aspect)*available)+.4f);
        float offset=PlannerBusy?-playerCamera.orthographicSize*playerCamera.aspect*.25f:screenPanel.activeSelf?playerCamera.orthographicSize*playerCamera.aspect*.25f:0;
        playerCamera.transform.SetPositionAndRotation(new Vector3((xmin+xmax)*.5f+offset,top+10,(zmin+zmax)*.5f),Quaternion.Euler(90,0,0));
    }
    public void ToggleScreenPanel(){if(useScreenUI&&screenPanel&&!PlannerBusy)screenPanel.SetActive(!screenPanel.activeSelf);}
    void SetupScreenUI()
    {
        screenReady=true;
        screenInput=player.GetComponent<StarterAssetsInputs>();
        if(screenInput)
        {
            savedCursorLocked=screenInput.cursorLocked;savedCursorLook=screenInput.cursorInputForLook;
            screenInput.cursorLocked=false;screenInput.cursorInputForLook=false;screenInput.LookInput(Vector2.zero);
        }
        savedRayEnabled=raycastInteractor.enabled;raycastInteractor.enabled=false;
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;
        screenPanel.SetActive(true);
        panelButtons=sceneUI.mainButtons;
        foreach(var button in panelButtons)
        {
            if(!button)continue;
            if(button.targetGraphic)button.targetGraphic.raycastTarget=true;
            foreach(var text in button.GetComponentsInChildren<TMP_Text>(true))text.raycastTarget=false;
        }
        screenActionBar=sceneUI.Get<RectTransform>("actions.root");
        panelToggle=sceneUI.Get<Button>("actions.controls");
        overviewToggle=sceneUI.Get<Button>("actions.overview");
        sitAction=sceneUI.Get<Button>("actions.sit");
        lieAction=sceneUI.Get<Button>("actions.lie");
        standAction=sceneUI.Get<Button>("actions.stand");
        furnitureAction=sceneUI.Get<Button>("actions.furniture");
        sceneUI.SetVisible("furniture.panel",false);
    }
    public void OpenFurniturePanel(){var editor=GetComponent<RoomComfortFurnitureEditor>();if(editor)editor.BeginEdit();}
    string M(string key,params string[] pairs)=>sceneUI?sceneUI.Message(key,pairs):RoomComfortSceneUI.Format(RoomComfortUIContent.Default(key),pairs);
    string StrengthText()=>M(level==0?"common.low":level==1?"common.medium":"common.high");
    bool PointerOverScreenUI()
    {
        if(!EventSystem.current||Mouse.current==null)return false;
        uiHits.Clear();var pointer=new PointerEventData(EventSystem.current){position=Mouse.current.position.ReadValue()};
        EventSystem.current.RaycastAll(pointer,uiHits);
        foreach(var hit in uiHits)if(hit.module is GraphicRaycaster)return true;
        return false;
    }
    void UpdateScreenInput()
    {
        pointerOverUI=PointerOverScreenUI();
        if(screenInput)
        {
            var mouse=Mouse.current;
            screenInput.LookInput(!editing&&!resting&&!overview&&!pointerOverUI&&mouse!=null&&mouse.rightButton.isPressed
                ?Vector2.Scale(mouse.delta.ReadValue(),new Vector2(.05f,-.05f)):Vector2.zero);
        }
        // Screen buttons replace the old centre-of-screen EZPZ button ray.
        raycastInteractor.enabled=false;
    }
    void UpdateScreenUI()
    {
        if(!screenReady)return;
        string hint=editing?(valid?"edit":"blocked"):PlannerBusy?"furniture":overview?"overview":resting?"resting":"walking";
        foreach(var id in new[]{"walking","resting","overview","furniture","edit","blocked"})sceneUI.SetVisible("hint."+id,id==hint);
        var stateText=sceneUI.Get<TMP_Text>("hint.state");
        stateText.gameObject.SetActive(!editing);
        sceneUI.Render(stateText,"wall",currentWall<0?M("common.chooseWall"):M("common.wall","wall",(currentWall+1).ToString()),
            "power",M(power?"common.on":"common.off"),"strength",StrengthText());
        var feedback=sceneUI.Get<TMP_Text>("hint.feedback");feedback.gameObject.SetActive(!editing);feedback.text=status;
        sceneUI.SetVisible("heading.normal",!editing);sceneUI.SetVisible("heading.edit",editing);
        foreach(var button in panelButtons)
        {
            if(!button)continue;
            bool placement=false,confirmButton=false,resetButton=false;
            for(int i=0;i<button.onClick.GetPersistentEventCount();i++)
            {
                string method=button.onClick.GetPersistentMethodName(i);
                placement|=method=="ConfirmPlacement"||method=="CancelPlacement"||method=="ResetPosition";
                confirmButton|=method=="ConfirmPlacement";resetButton|=method=="ResetPosition";
            }
            button.gameObject.SetActive(placement?editing:!editing);
            button.interactable=!confirmButton||valid;
        }
        sceneUI.SetVisible("actions.controls.hide",screenPanel.activeSelf);sceneUI.SetVisible("actions.controls.show",!screenPanel.activeSelf);
        sceneUI.SetVisible("actions.overview.enter",!overview);sceneUI.SetVisible("actions.overview.return",overview);
        overviewToggle.interactable=!editing&&!PlannerBusy;
        panelToggle.interactable=!PlannerBusy;
        sitAction.interactable=!editing&&!resting&&!overview&&Near(seatView);
        lieAction.interactable=!editing&&!resting&&!overview&&Near(bedView);
        standAction.interactable=resting&&!editing;
        sitAction.gameObject.SetActive(!resting);lieAction.gameObject.SetActive(!resting);furnitureAction.gameObject.SetActive(!resting);
        standAction.gameObject.SetActive(true);
        // A HorizontalLayoutGroup in the scene arranges the visible buttons.
        Set(seatPrompt,false);Set(bedPrompt,false);
    }


    public void Notify(string message){status=message;}
    public bool BeginFurnitureEdit()
    {
        if(!useScreenUI||PlannerBusy||editing||resting){status=M("flow.beforeFurniture");return false;}
        if(!overview)ToggleOverview();
        PlannerBusy=true;panelBeforePlanner=screenPanel.activeSelf;screenPanel.SetActive(false);
        status=M("flow.beginFurniture");return true;
    }
    public void EndFurnitureEdit(bool accepted)
    {
        PlannerBusy=false;screenPanel.SetActive(panelBeforePlanner);
        if(currentWall>=0){ConfigureWall(currentWall);SetPosition(acRoot.position);if(!valid){power=false;coverage.gameObject.SetActive(false);}}
        status=accepted?M("flow.furnitureDone"):M("flow.furnitureCancel");
    }
    [System.Serializable] public struct ACLayoutState
    { public int wall,strength;public Vector3 position;public bool on; }
    public ACLayoutState CaptureACLayout()=>new ACLayoutState{wall=currentWall,strength=level,position=acRoot.position,on=power};
    public void RestoreACLayout(ACLayoutState state)
    {
        if(editing||PlannerBusy)return;
        level=Mathf.Clamp(state.strength,0,2);power=false;coverage.gameObject.SetActive(false);
        if(state.wall<0){currentWall=-1;confirmed=false;acRoot.gameObject.SetActive(false);ShowHint(1);return;}
        currentWall=state.wall;if(!ConfigureWall(currentWall))return;
        acRoot.gameObject.SetActive(true);SetPosition(state.position);confirmed=true;
        remembered[currentWall]=true;wallPositions[currentWall]=acRoot.position;
        ApplyStrength();power=state.on&&valid;coverage.gameObject.SetActive(power);ShowHint(power?3:2);
    }
    bool FindSafeExit(Vector3 preferred,out Vector3 result)
    {
        var offsets=new[]{Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,
            new Vector3(1,0,1),new Vector3(-1,0,1),new Vector3(1,0,-1),new Vector3(-1,0,-1)};
        foreach(var offset in offsets){var candidate=preferred+offset*.8f;if(ClearStandingPoint(candidate)){result=candidate;return true;}}
        if(ClearStandingPoint(safeSpawn)){result=safeSpawn;return true;}
        GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
        for(float x=xmin+.5f;x<xmax-.5f;x+=.6f)for(float z=zmin+.5f;z<zmax-.5f;z+=.6f)
        {var candidate=new Vector3(x,safeSpawn.y,z);if(ClearStandingPoint(candidate)){result=candidate;return true;}}
        result=preferred;return false;
    }
    public bool ResolveStandingSpace()
    {
        if(resting)return false;
        Physics.SyncTransforms();
        if(!FindSafeExit(player.position,out var destination))return false;
        Vector3 delta=destination-player.position;
        bool wasOn=character&&character.enabled;if(character)character.enabled=false;
        player.position=destination;if(character)character.enabled=wasOn;
        if(overview)cameraPosition+=delta;
        return true;
    }
    bool ClearStandingPoint(Vector3 p)
    {
        GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
        float radius=character?character.radius:.25f,height=character?character.height:2;
        if(p.x<xmin+radius+.05f||p.x>xmax-radius-.05f||p.z<zmin+radius+.05f||p.z>zmax-radius-.05f)return false;
        var centre=p+(character?character.center:Vector3.up);
        float half=Mathf.Max(0,height*.5f-radius);
        foreach(var hit in Physics.OverlapCapsule(centre-Vector3.up*half,centre+Vector3.up*half,radius,~0,QueryTriggerInteraction.Ignore))
            if(!hit.transform.IsChildOf(player))return false;
        return true;
    }
    void RecoverOutsideRoom()
    {
        if(editing||overview||resting)return;
        GetRoomLimits(out float xmin,out float xmax,out float zmin,out float zmax);
        var p=player.position;
        if(p.y>safeSpawn.y-2&&p.x>xmin-.5f&&p.x<xmax+.5f&&p.z>zmin-.5f&&p.z<zmax+.5f)return;
        if(!FindSafeExit(safeSpawn,out var safe))return;
        bool wasOn=character&&character.enabled;if(character)character.enabled=false;
        player.position=safe;if(character)character.enabled=wasOn;
        status=M("flow.recovered");
    }

    void OnDestroy(){
        SetOverviewVisibility(false);
        if(screenReady){
            if(screenInput){screenInput.cursorLocked=savedCursorLocked;screenInput.cursorInputForLook=savedCursorLook;screenInput.LookInput(Vector2.zero);}
            if(raycastInteractor)raycastInteractor.enabled=savedRayEnabled;
            // Scene UI belongs to the scene and is never destroyed here.
        }
        if(mountingOutline&&mountingOutline.sharedMaterial)Destroy(mountingOutline.sharedMaterial);if(overlayBackground)Destroy(overlayBackground);}
}
