using NUnit.Framework;
using UnityEngine;

public sealed class MenuAngularMotionTests
{
    [TestCase(1,0)] [TestCase(-1,0)] [TestCase(0,1)] [TestCase(0,-1)]
    public void GyroMovesTheMenuWithoutAnySwordTipTravelOrRecenterDependency(int horizontal,int vertical)
    {
        foreach(Quaternion pose in new[]{Quaternion.identity,Quaternion.Euler(90,30,70),Quaternion.Euler(-45,-80,140)})
        {
            var menu=new MenuGestureNavigation();
            menu.Read(pose,Vector3.zero,true,true,false,0,0);
            Vector3 gyro=new Vector3(-vertical*480,horizontal*480,0);
            Assert.That(menu.Read(pose,gyro,true,true,false,0,.02), Is.EqualTo(new Vector2Int(horizontal,vertical)));
        }
    }
    [Test]
    public void BelowThresholdDiagonalMotionStillArmsTheNextFlick()
    {
        var menu=new MenuGestureNavigation();
        menu.Read(Quaternion.identity,new Vector3(300,300,0),true,true,false,0,0);
        Assert.That(menu.Read(Quaternion.identity,Vector3.up*500,true,true,false,0,.02),Is.EqualTo(Vector2Int.right));
    }
    [Test]
    public void PhoneWorldVelocityAndHardwareLocalVelocityKeepTheSameScreenSigns()
    {
        foreach(Quaternion rotation in new[]{Quaternion.identity,Quaternion.Euler(30,60,120)})
        {
            Vector3 menuVelocity=new Vector3(-500,700,0);
            Vector3 phoneGyro=Quaternion.Inverse(rotation)*Quaternion.Euler(90,0,0)*menuVelocity;
            Assert.That(MenuAngularMotion.Project(rotation,rotation,phoneGyro,true,.02f,true), Is.EqualTo(new Vector2(700,500)).Using(Vector2Comparer.Instance));
        }
    }
    sealed class Vector2Comparer : System.Collections.Generic.IEqualityComparer<Vector2>
    {
        public static readonly Vector2Comparer Instance=new Vector2Comparer();
        public bool Equals(Vector2 a,Vector2 b)=>Vector2.Distance(a,b)<.001f;
        public int GetHashCode(Vector2 value)=>0;
    }
}
