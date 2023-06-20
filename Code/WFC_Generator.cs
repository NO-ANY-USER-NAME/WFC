using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Unity.Collections;
using Unity.Burst;
using Unity.Jobs;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Tilemaps;
using Random=UnityEngine.Random;


public class WFC_Generator:MonoBehaviour{
    public const int UP=0,LEFT=1,RIGHT=2,DOWN=3;
    public Grid grid;
    public Tilemap targetMap;
    public Vector2Int mapSize;
    [System.Serializable]
    public class Rule{
        public Tile tile;
        public List<Tile> validUp;
        public List<Tile> validDown;
        public List<Tile> validLeft;
        public List<Tile> validRight;
    }
    [Header("tiles")]public List<Rule> tileChoices;

    private Vector3 cellSize;
    private ushort[][] map;
    private Dictionary<Tile,ushort> tileToIndex;//i dont believe someone will have >65535 different tiles in his game...
    private NativeArray<ushort> nativeMap;
    private NativeArray<ushort> validUp,validDown,validLeft,validRight;
    private NativeArray<int> upLength,downLength,leftLength,rightLength;
    //perfix sum array
    //for the rules describle i-th tile, the bound is stored at the format [i,i+1)


    void Awake(){
        cellSize=grid.cellSize;
        tileToIndex=new Dictionary<Tile, ushort>(tileChoices.Count<<1);
        map=new ushort[mapSize.y][];
        int i;
        for(i=0;i<mapSize.y;i++){
            map[i]=new ushort[mapSize.x];
        }
        HashTiles();
        InitializeNative();
        //Collapse();
    }
    private void HashTiles(){
        ushort i;
        for(i=0;i<tileChoices.Count;i++){
            if(tileToIndex.TryAdd(tileChoices[i].tile,i)){
            }
            else{
                Debug.Log($"duplicate rule of tile in {tileToIndex[tileChoices[i].tile]} and {i}");
                throw new Exception("repeated tile rule error");
            }
        }
    }
    private void InitializeNative(){
        int count=tileChoices.Count;
        nativeMap=new NativeArray<ushort>(mapSize.x*mapSize.y,Allocator.Persistent);
        downLength=new NativeArray<int>(count+1,Allocator.Persistent);
        upLength=new NativeArray<int>(count+1,Allocator.Persistent);
        leftLength=new NativeArray<int>(count+1,Allocator.Persistent);
        rightLength=new NativeArray<int>(count+1,Allocator.Persistent);

        downLength[0]=0;
        upLength[0]=0;
        leftLength[0]=0;
        rightLength[0]=0;
        int i,j;
        for(i=0;i<count;i=j){
            j=i+1;
            Rule curRule=tileChoices[i];
            downLength[j]=downLength[i]+curRule.validDown.Count;
            leftLength[j]=leftLength[i]+curRule.validLeft.Count;
            rightLength[j]=rightLength[i]+curRule.validRight.Count;
            upLength[j]=upLength[i]+curRule.validUp.Count;
        }

        validUp=new NativeArray<ushort>(upLength[count],Allocator.Persistent);
        validDown=new NativeArray<ushort>(downLength[count],Allocator.Persistent);
        validLeft=new NativeArray<ushort>(leftLength[count],Allocator.Persistent);
        validRight=new NativeArray<ushort>(rightLength[count],Allocator.Persistent);

        for(i=0;i<count;i++){
            Rule curRule=tileChoices[i];
            List<Tile> valid;
            int k;
            valid=curRule.validDown;
            for(j=downLength[i],k=0;j<downLength[i+1];j++,k++){
                validDown[j]=tileToIndex[valid[k]];
            }
            valid=curRule.validUp;
            for(j=upLength[i],k=0;j<upLength[i+1];j++,k++){
                validUp[j]=tileToIndex[valid[k]];
            }
            valid=curRule.validLeft;
            for(j=leftLength[i],k=0;j<leftLength[i+1];j++,k++){
                validLeft[j]=tileToIndex[valid[k]];
            }
            valid=curRule.validRight;
            for(j=rightLength[i],k=0;j<rightLength[i+1];j++,k++){
                validRight[j]=tileToIndex[valid[k]];
            }
        }//flatten
    }

    
    private void Collapse(){
        List<Tile> validUp,validRight;
        Dictionary<Tile,bool> seen=new Dictionary<Tile, bool>();
        List<ushort> validUpRight=new List<ushort>();
        Vector3 center=transform.position;
        Vector3 pos;
        Vector2 start;
        int i,j;
        int choice;
        pos.z=center.z;
        start.y=center.y-mapSize.y*cellSize.y/2;
        start.x=center.x-mapSize.x*cellSize.x/2;
        
        targetMap.ClearAllTiles();

        pos.y=(start.y);
        pos.x=(start.x);
        choice=Random.Range(0,tileChoices.Count);
        map[0][0]=(ushort)choice;
        targetMap.SetTile(grid.WorldToCell(pos),tileChoices[choice].tile);

        for(j=1;j<mapSize.x;j++){
            pos.x=(j*cellSize.x+start.x);
            validRight=tileChoices[map[0][j-1]].validRight;
            
            choice=Random.Range(0,validRight.Count);
            map[0][j]=tileToIndex[validRight[choice]];
            targetMap.SetTile(grid.WorldToCell(pos),validRight[choice]);
        }//the tiles placed are getting righter, so refer to previous valid right

        for(i=1;i<mapSize.y;i++){
            pos.y=(i*cellSize.y+start.y);
            pos.x=(start.x);
            validUp=tileChoices[map[i-1][0]].validUp;
            
            choice=Random.Range(0,validUp.Count);
            map[i][0]=tileToIndex[validUp[choice]];
            targetMap.SetTile(grid.WorldToCell(pos),validUp[choice]);

            for(j=1;j<mapSize.x;j++){
                pos.x=(j*cellSize.x+start.x);
                validUp=tileChoices[map[i-1][j]].validUp;
                validRight=tileChoices[map[i][j-1]].validRight;

                for(int k=0;k<validUp.Count;k++){
                    seen.TryAdd(validUp[k],true);
                }
                for(int k=0;k<validRight.Count;k++){
                    if(seen.ContainsKey(validRight[k])){
                        validUpRight.Add(tileToIndex[validRight[k]]);
                    }
                }

                choice=Random.Range(0,validUpRight.Count);
                choice=validUpRight[choice];
                map[i][j]=(ushort)choice;
                targetMap.SetTile(grid.WorldToCell(pos),tileChoices[choice].tile);
                validUpRight.Clear();
                seen.Clear();
            }
        }
    }

    private int counter=0;
    private void Update(){
        counter=(counter+1)&0x1FF;
        if(counter==0){
            Debug.Log("generate");
            Collapse();
        }
    }


    private void SetBaesdOnMap(){
        Vector3 pos,start,center=transform.position;
        int i,j;
        pos.z=center.z;
        start.y=center.y-mapSize.y*cellSize.y/2;
        start.x=center.x-mapSize.x*cellSize.x/2;

        for(i=0;i<mapSize.y;i++){
            pos.y=(i*cellSize.y+start.y);
            for(j=0;j<mapSize.x;j++){
                pos.x=(j*cellSize.x+start.x);
                targetMap.SetTile(grid.WorldToCell(pos),tileChoices[map[i][j]].tile);
            }
        }
    }

    //Helper function, i and j will iterate in bound [bottom,top) and [left,right)
  #pragma warning disable CS1998

    private async void Collapse(bool withTask){
        System.Random random=new System.Random(Environment.TickCount);
        int i,j;
        int centerI=(mapSize.y)>>1,centerJ=(mapSize.x)>>1;
        map[centerI][centerJ]=(ushort)random.Next(tileChoices.Count);
        for(i=centerI-1;i>=0;i--){
            List<Tile> validDown=tileChoices[map[i+1][centerJ]].validDown;
            map[i][centerJ]=tileToIndex[validDown[random.Next(validDown.Count)]];
        }

        for(i=centerI+1;i<mapSize.y;i++){
            List<Tile> validUp=tileChoices[map[i-1][centerJ]].validUp;
            map[i][centerJ]=tileToIndex[validUp[random.Next(validUp.Count)]];
        }

        for(j=centerJ-1;j>=0;j--){
            List<Tile> validLeft=tileChoices[map[centerI][j+1]].validLeft;
            map[centerI][j]=tileToIndex[validLeft[random.Next(validLeft.Count)]];
        }

        for(j=centerJ+1;j<mapSize.x;j++){
            List<Tile> validRight=tileChoices[map[centerI][j-1]].validRight;
            map[centerI][j]=tileToIndex[validRight[random.Next(validRight.Count)]];
        }

        Task[] tasks=new Task[4];
        tasks[0]=Task.Run(()=>CollapseBottomLeft(0,centerI,0,centerJ));
        tasks[1]=Task.Run(()=>CollapseBottomRight(0,centerI,centerJ+1,mapSize.x));
        tasks[2]=Task.Run(()=>CollapseTopLeft(centerI+1,mapSize.y,0,centerJ));
        tasks[3]=Task.Run(()=>CollapseTopRight(centerI+1,mapSize.y,centerJ+1,mapSize.x));

        await Task.WhenAll(tasks);
    }

    private async void CollapseBottomLeft(int bottom,int top,int left,int right){
        int i,j;
        Dictionary<Tile,bool> seen=new Dictionary<Tile,bool>();
        List<ushort> validChoices=new List<ushort>();
        List<Tile> validDown,validLeft;
        System.Random random=new System.Random(Environment.TickCount);
        int choice;
        
        for(i=top-1;i>=bottom;i--){
            for(j=right-1;j>=left;j--){
                validDown=tileChoices[map[i+1][j]].validDown;
                validLeft=tileChoices[map[i][j+1]].validLeft;
                
                for(int k=0;k<validLeft.Count;k++){
                    seen.TryAdd(validLeft[k],true);
                }
                for(int k=0;k<validDown.Count;k++){
                    if(seen.ContainsKey(validDown[k])){
                        validChoices.Add(tileToIndex[validDown[k]]);
                    }
                }

                choice=random.Next(validChoices.Count);
                choice=validChoices[choice];
                map[i][j]=(ushort)choice;
                validChoices.Clear();
                seen.Clear();
            }
        }
    }

    private async void CollapseBottomRight(int bottom,int top,int left,int right){
        int i,j;
        Dictionary<Tile,bool> seen=new Dictionary<Tile,bool>();
        List<ushort> validChoices=new List<ushort>();
        List<Tile> validDown,validRight;
        System.Random random=new System.Random(Environment.TickCount);
        int choice;
        
        for(i=top-1;i>=bottom;i--){
            for(j=left;j<right;j++){
                validDown=tileChoices[map[i+1][j]].validDown;
                validRight=tileChoices[map[i][j-1]].validRight;
                
                for(int k=0;k<validRight.Count;k++){
                    seen.TryAdd(validRight[k],true);
                }
                for(int k=0;k<validDown.Count;k++){
                    if(seen.ContainsKey(validDown[k])){
                        validChoices.Add(tileToIndex[validDown[k]]);
                    }
                }

                choice=random.Next(validChoices.Count);
                choice=validChoices[choice];
                map[i][j]=(ushort)choice;
                validChoices.Clear();
                seen.Clear();
            }
        }
    }

    private async void CollapseTopLeft(int bottom,int top,int left,int right){
        int i,j;
        Dictionary<Tile,bool> seen=new Dictionary<Tile,bool>();
        List<ushort> validChoices=new List<ushort>();
        List<Tile> validUp,validLeft;
        System.Random random=new System.Random(Environment.TickCount);
        int choice;
        
        for(i=bottom;i<top;i++){
            for(j=right-1;j>=left;j--){
                validUp=tileChoices[map[i-1][j]].validUp;
                validLeft=tileChoices[map[i][j+1]].validLeft;
                
                for(int k=0;k<validLeft.Count;k++){
                    seen.TryAdd(validLeft[k],true);
                }
                for(int k=0;k<validUp.Count;k++){
                    if(seen.ContainsKey(validUp[k])){
                        validChoices.Add(tileToIndex[validUp[k]]);
                    }
                }

                choice=random.Next(validChoices.Count);
                choice=validChoices[choice];
                map[i][j]=(ushort)choice;
                validChoices.Clear();
                seen.Clear();
            }
        }
    }

    private async void CollapseTopRight(int bottom,int top,int left,int right){
        int i,j;
        Dictionary<Tile,bool> seen=new Dictionary<Tile,bool>();
        List<ushort> validChoices=new List<ushort>();
        List<Tile> validUp,validRight;
        System.Random random=new System.Random(Environment.TickCount);
        int choice;
        
        for(i=bottom;i<top;i++){
            for(j=left;j<right;j++){
                validUp=tileChoices[map[i-1][j]].validUp;
                validRight=tileChoices[map[i][j-1]].validRight;
                
                for(int k=0;k<validRight.Count;k++){
                    seen.TryAdd(validRight[k],true);
                }
                for(int k=0;k<validUp.Count;k++){
                    if(seen.ContainsKey(validUp[k])){
                        validChoices.Add(tileToIndex[validUp[k]]);
                    }
                }

                choice=random.Next(validChoices.Count);
                choice=validChoices[choice];
                map[i][j]=(ushort)choice;
                validChoices.Clear();
                seen.Clear();
            }
        }
    }
  #pragma warning restore CS1998
    
    private void DisposeAll(){
        nativeMap.Dispose();
        validDown.Dispose();
        validUp.Dispose();
        validLeft.Dispose();
        validRight.Dispose();
        downLength.Dispose();
        upLength.Dispose();
        leftLength.Dispose();
        rightLength.Dispose();
    }

    void OnDiable(){
        DisposeAll();
    }

    void OnDestroy(){
        DisposeAll();
    }


    public struct CollapseBottomLeftJob:IJob{
        [ReadOnly]NativeArray<ushort> validLeft,validDown;
        [ReadOnly]NativeArray<int> leftLength,downLength;
        [NativeDisableContainerSafetyRestriction]NativeArray<ushort> map;
        [NativeDisableContainerSafetyRestriction]NativeParallelHashMap<ushort,bool> seen;
        [NativeDisableContainerSafetyRestriction]NativeList<ushort> validChoices;

        int bottom,top,left,right;//bound for iteration
        int columnNumber;
        public void Execute(){
            int i,j;
            int choice;
            for(i=top-1;i>=bottom;i--){
                for(j=right-1;j>=left;j--){
                    
                    choice=map[i*columnNumber+(j+1)];
                    for(int k=leftLength[choice];k<leftLength[choice+1];k++){
                        seen.TryAdd(validLeft[k],true);
                    }

                    choice=map[(i+1)*columnNumber+j];
                    for(int k=downLength[choice];k<downLength[choice+1];k++){
                        if(seen.ContainsKey(validDown[k])){
                            validChoices.Add(validDown[k]);
                        }
                    }

                    choice=Random.Range(0,validChoices.Length);
                    choice=validChoices[choice];
                    map[i*columnNumber+j]=(ushort)choice;
                    validChoices.Clear();
                    seen.Clear();
                }
            }
            validChoices.Dispose();
            seen.Dispose();
        }
    }


    public struct CollapseBottomRightJob:IJob{
        [ReadOnly]NativeArray<ushort> validRight,validDown;
        [ReadOnly]NativeArray<int> rightLength,downLength;
        [NativeDisableContainerSafetyRestriction]NativeArray<ushort> map;
        [NativeDisableContainerSafetyRestriction]NativeParallelHashMap<ushort,bool> seen;
        [NativeDisableContainerSafetyRestriction]NativeList<ushort> validChoices;

        int bottom,top,left,right;//bound for iteration
        int columnNumber;
        public void Execute(){
            int i,j;
            int choice;
            for(i=top-1;i>=bottom;i--){
                for(j=left;j<right;j++){

                    choice=map[i*columnNumber+(j-1)];
                    for(int k=rightLength[choice];k<rightLength[choice+1];k++){
                        seen.TryAdd(validRight[k],true);
                    }

                    choice=map[(i+1)*columnNumber+j];
                    for(int k=downLength[choice];k<downLength[choice+1];k++){
                        if(seen.ContainsKey(validDown[k])){
                            validChoices.Add(validDown[k]);
                        }
                    }

                    choice=Random.Range(0,validChoices.Length);
                    choice=validChoices[choice];
                    map[i*columnNumber+j]=(ushort)choice;
                    validChoices.Clear();
                    seen.Clear();
                }
            }
            validChoices.Dispose();
            seen.Dispose();
        }
    }


    public struct CollapseTopLeftJob:IJob{
        [ReadOnly]NativeArray<ushort> validLeft,validUp;
        [ReadOnly]NativeArray<int> leftLength,upLength;
        [NativeDisableContainerSafetyRestriction]NativeArray<ushort> map;
        [NativeDisableContainerSafetyRestriction]NativeParallelHashMap<ushort,bool> seen;
        [NativeDisableContainerSafetyRestriction]NativeList<ushort> validChoices;

        int bottom,top,left,right;//bound for iteration
        int columnNumber;
        public void Execute(){
            int i,j;
            int choice;
            for(i=bottom;i<top;i++){
                for(j=right-1;j>=left;j--){

                    choice=map[i*columnNumber+(j+1)];
                    for(int k=leftLength[choice];k<leftLength[choice+1];k++){
                        seen.TryAdd(validLeft[k],true);
                    }

                    choice=map[(i-1)*columnNumber+j];
                    for(int k=upLength[choice];k<upLength[choice+1];k++){
                        if(seen.ContainsKey(validUp[k])){
                            validChoices.Add(validUp[k]);
                        }
                    }

                    choice=Random.Range(0,validChoices.Length);
                    choice=validChoices[choice];
                    map[i*columnNumber+j]=(ushort)choice;
                    validChoices.Clear();
                    seen.Clear();
                }
            }
            validChoices.Dispose();
            seen.Dispose();
        }
    }


    public struct CollapseTopRightJob:IJob{
        [ReadOnly]NativeArray<ushort> validRight,validUp;
        [ReadOnly]NativeArray<int> rightLength,upLength;
        [NativeDisableContainerSafetyRestriction]NativeArray<ushort> map;
        [NativeDisableContainerSafetyRestriction]NativeParallelHashMap<ushort,bool> seen;
        [NativeDisableContainerSafetyRestriction]NativeList<ushort> validChoices;

        int bottom,top,left,right;//bound for iteration
        int columnNumber;
        public void Execute(){
            int i,j;
            int choice;
            for(i=bottom;i<top;i++){
                for(j=right-1;j>=left;j--){

                    choice=map[i*columnNumber+(j-1)];
                    for(int k=rightLength[choice];k<rightLength[choice+1];k++){
                        seen.TryAdd(validRight[k],true);
                    }

                    choice=map[(i-1)*columnNumber+j];
                    for(int k=upLength[choice];k<upLength[choice+1];k++){
                        if(seen.ContainsKey(validUp[k])){
                            validChoices.Add(validUp[k]);
                        }
                    }

                    choice=Random.Range(0,validChoices.Length);
                    choice=validChoices[choice];
                    map[i*columnNumber+j]=(ushort)choice;
                    validChoices.Clear();
                    seen.Clear();
                }
            }
            validChoices.Dispose();
            seen.Dispose();
        }
    }

}


