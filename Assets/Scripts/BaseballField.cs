using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Converts only the instantiated stadium. Its imported meshes and materials
// remain available unchanged for the original cricket asset.
public sealed class BaseballField : MonoBehaviour
{
    private readonly List<Mesh> meshes = new List<Mesh>();
    private readonly List<Material> materials = new List<Material>();
    private GameObject markings;

    public void Initialize(GameObject environment)
    {
        if(environment==null || markings!=null)return;
        Renderer[] existing=environment.GetComponentsInChildren<Renderer>(true);
        Renderer ground=null;
        foreach(Renderer renderer in existing)
            if(renderer.name=="Ground .009")ground=renderer;
        if(ground==null)return;

        Material grass=Material("Baseball grass",new Color(.39f,.56f,.24f));
        Material boards=Material("Baseball boundary",new Color(.12f,.49f,.72f));
        Material scoreboard=Material("Baseball scoreboard",new Color(.08f,.25f,.36f));
        foreach(Renderer renderer in existing)
        {
            // The six cylinders are the two sets of wickets and their bails;
            // the unsuffixed Ground mesh contains the raised cricket pitch.
            if(renderer.name.Trim()=="Ground" || IsWicket(renderer.name))
            {renderer.enabled=false;continue;}
            Material[] slots=renderer.sharedMaterials;
            bool changed=false;
            for(int i=0;i<slots.Length;i++)
            {
                if(slots[i]==null)continue;
                string name=slots[i].name.Replace(' ','_');
                if(name=="Pitch" || name=="Material.003")slots[i]=grass;
                else if(name=="Boundary_texture")slots[i]=boards;
                else if(name=="Scoreboard_screen")slots[i]=scoreboard;
                else continue;
                changed=true;
            }
            if(changed)renderer.sharedMaterials=slots;
        }

        markings=new GameObject("Baseball field markings");
        markings.transform.SetParent(environment.transform,false);
        Bounds floor=ground.bounds;
        markings.transform.position=new Vector3(floor.center.x,floor.max.y+.015f,floor.center.z);
        Material dirt=Material("Baseball infield dirt",new Color(.69f,.43f,.25f));
        Material chalk=Material("Baseball chalk",new Color(.99f,.99f,.95f));
        Vector3 home=new Vector3(0f,0f,-4f),first=new Vector3(8f,0f,4f);
        Vector3 second=new Vector3(0f,0f,12f),third=new Vector3(-8f,0f,4f);
        Vector3 center=new Vector3(0f,0f,4f);
        Polygon("Infield diamond",new[]{home,third,second,first},dirt);
        Polygon("Infield grass",new[]{Vector3.Lerp(center,home,.68f),Vector3.Lerp(center,third,.68f),
            Vector3.Lerp(center,second,.68f),Vector3.Lerp(center,first,.68f)},grass,.006f);
        Disc("Home plate dirt",home,1.3f,dirt,.004f);
        Base("First base",first,chalk);Base("Second base",second,chalk);Base("Third base",third,chalk);
        Polygon("Home plate",new[]{home+new Vector3(0f,0f,-.35f),home+new Vector3(-.36f,0f,0f),
            home+new Vector3(-.36f,0f,.38f),home+new Vector3(.36f,0f,.38f),home+new Vector3(.36f,0f,0f)},chalk,.028f);
        Line("First base foul line",home,home+new Vector3(21f,0f,21f),.11f,chalk);
        Line("Third base foul line",home,home+new Vector3(-21f,0f,21f),.11f,chalk);
        BatterBox("Left batter box",home+Vector3.left*.9f,chalk);
        BatterBox("Right batter box",home+Vector3.right*.9f,chalk);
        Mound(center,dirt);
        Polygon("Pitcher rubber",new[]{center+new Vector3(-.4f,0f,-.09f),center+new Vector3(-.4f,0f,.09f),
            center+new Vector3(.4f,0f,.09f),center+new Vector3(.4f,0f,-.09f)},chalk,.17f);
    }

    private static bool IsWicket(string name)
    {
        switch(name)
        {
            case "Cylinder.001":case "Cylinder.002":case "Cylinder.003":
            case "Cylinder.004":case "Cylinder.005":case "Cylinder.006":return true;
            default:return false;
        }
    }

    private Material Material(string name,Color color)
    {
        var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name,color=color};
        material.SetFloat("_Smoothness",.08f);materials.Add(material);return material;
    }

    private void Base(string name,Vector3 center,Material material)
    {
        const float half=.38f;
        Polygon(name,new[]{center+new Vector3(-half,0f,-half),center+new Vector3(-half,0f,half),
            center+new Vector3(half,0f,half),center+new Vector3(half,0f,-half)},material,.03f);
    }

    private void BatterBox(string name,Vector3 center,Material material)
    {
        Vector3 a=center+new Vector3(-.32f,0f,-.7f),b=center+new Vector3(-.32f,0f,.7f);
        Vector3 c=center+new Vector3(.32f,0f,.7f),d=center+new Vector3(.32f,0f,-.7f);
        Line(name+" left",a,b,.065f,material);Line(name+" front",b,c,.065f,material);
        Line(name+" right",c,d,.065f,material);Line(name+" back",d,a,.065f,material);
    }

    private void Line(string name,Vector3 start,Vector3 end,float width,Material material)
    {
        Vector3 side=Vector3.Cross(Vector3.up,end-start).normalized*width*.5f;
        Polygon(name,new[]{start-side,end-side,end+side,start+side},material,.02f);
    }

    private void Disc(string name,Vector3 center,float radius,Material material,float height)
    {
        const int segments=48;var vertices=new Vector3[segments];
        for(int i=0;i<segments;i++)
        {
            float angle=-i*Mathf.PI*2f/segments;
            vertices[i]=center+new Vector3(Mathf.Cos(angle)*radius,0f,Mathf.Sin(angle)*radius);
        }
        Polygon(name,vertices,material,height);
    }

    private void Mound(Vector3 center,Material material)
    {
        const int segments=48;var vertices=new Vector3[segments+1];var triangles=new int[segments*3];
        vertices[0]=center+Vector3.up*.16f;
        for(int i=0;i<segments;i++)
        {
            float angle=-i*Mathf.PI*2f/segments;
            vertices[i+1]=center+new Vector3(Mathf.Cos(angle)*1.05f,.01f,Mathf.Sin(angle)*1.05f);
            triangles[i*3]=0;triangles[i*3+1]=i+1;triangles[i*3+2]=(i+1)%segments+1;
        }
        Surface("Pitcher mound",vertices,triangles,material);
    }

    private void Polygon(string name,Vector3[] vertices,Material material,float height=0f)
    {
        for(int i=0;i<vertices.Length;i++)vertices[i]+=Vector3.up*height;
        var triangles=new int[(vertices.Length-2)*3];
        bool reverse=Vector3.Cross(vertices[1]-vertices[0],vertices[2]-vertices[0]).y<0f;
        for(int i=0;i<vertices.Length-2;i++)
        {
            triangles[i*3]=0;triangles[i*3+1]=reverse?i+2:i+1;triangles[i*3+2]=reverse?i+1:i+2;
        }
        Surface(name,vertices,triangles,material);
    }

    private void Surface(string name,Vector3[] vertices,int[] triangles,Material material)
    {
        var mesh=new Mesh{name=name,vertices=vertices,triangles=triangles};
        mesh.RecalculateNormals();mesh.RecalculateBounds();meshes.Add(mesh);
        var surface=new GameObject(name,typeof(MeshFilter),typeof(MeshRenderer));
        surface.transform.SetParent(markings.transform,false);
        surface.GetComponent<MeshFilter>().sharedMesh=mesh;
        MeshRenderer renderer=surface.GetComponent<MeshRenderer>();renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off;
    }

    private void OnDestroy()
    {
        if(markings!=null)Destroy(markings);
        foreach(Mesh mesh in meshes)if(mesh!=null)Destroy(mesh);
        foreach(Material material in materials)if(material!=null)Destroy(material);
    }
}
