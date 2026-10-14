using System;
using Unity.VisualScripting;
using UnityEngine;

public class BuildingModelSpawn : MonoBehaviour
{
    // spawne model prefabu při spawnu jakékoliv budovy podle vlastnictví, aby se lišily hráči
    // zelená ai, modrá lokální hráč, červená protihráč, žlutá třeba další hráč
    [SerializeField] GameObject red; 
    [SerializeField] GameObject green; 
    [SerializeField] GameObject yellow; 
    [SerializeField] GameObject blue; 
    [SerializeField] GameObject spawnParent; 

    void Start()
    {
        
    }
    public void spawnPrefabModel(bool isOwner, int id)
    {
        if(isOwner)
        {
            Instantiate(blue, spawnParent.transform,false);
            return;
            }
            
        switch(id)
        {
            case 1:
                Instantiate(red, spawnParent.transform,false);
                break;
            case 0:
                Instantiate(green, spawnParent.transform,false);
                break;
        }
    }


    // Update is called once per frame
    void Update()
    {
        
    }
}
