using Unity.Netcode;
using UnityEngine;
using System.Collections;

public class LaserTower : Building
{
    // Určuje typ této budovy.
    // Building systém tak může poznat, že jde o LaserTower.
    public override BuildingType Type => BuildingType.LaserTower;


    // ============================================================
    //  LASER TOWER – CÍLENÍ
    // ============================================================

    [Header("Laser Tower – cílení")]

    // Objekt, který se bude otáčet směrem k cíli.
    // Například otočná horní část věže.
    [Tooltip("Transform otočné části věže (turret/hlaveň), která se natáčí na cíl.")]
    [SerializeField] private Transform turretPivot;


    // Místo, odkud vychází laser.
    // Používá se také pro particle efekt.
    [Tooltip("Bod, odkud vychází laser paprsek (a particle efekt).")]
    [SerializeField] private Transform firePoint;


    // Určuje, na jakých Unity layerech bude věž hledat nepřátele.
    // Pokud například jednotka není na některém z těchto layerů,
    // Physics.OverlapSphere ji vůbec nenajde.
    [Tooltip("Layer maska pro detekci nepřátelských jednotek a budov.")]
    [SerializeField] private LayerMask enemyLayer;


    // Jak často se má věž znovu podívat do okolí a hledat nepřítele.
    // 0.25 = každých 0,25 sekundy.
    [Tooltip("Jak často (v sekundách) se skenuje okolí.")]
    [SerializeField] private float enemyScanInterval = 0.25f;


    // Maximální rychlost otáčení věže.
    // Vyšší číslo = rychlejší otáčení.
    [Tooltip("Rychlost rotace turetu (stupně/s). Vyšší = plynulejší zamíření.")]
    [SerializeField] private float turretRotationSpeed = 180f;


    // ============================================================
    //  LASER TOWER – VFX / SFX
    // ============================================================

    [Header("Laser Tower – VFX/SFX")]

    // LineRenderer představuje samotný laserový paprsek.
    // Jeho začátek bude firePoint a konec bude pozice cíle.
    [Tooltip("LineRenderer pro laser paprsek (child objekt pod firePoint).")]
    [SerializeField] private LineRenderer laserBeam;


    // Jak dlouho zůstane laser po výstřelu viditelný.
    [Tooltip("Jak dlouho je laser paprsek viditelný po výstřelu (v sekundách).")]
    [SerializeField] private float laserDisplayDuration = 0.15f;


    // Volitelný particle efekt při výstřelu.
    [Tooltip("Particle system výstřelu (volitelné – stejný princip jako u Photonu).")]
    [SerializeField] private ParticleSystem shotParticle;


    // Zvuk výstřelu.
    [Tooltip("Zvuk výstřelu.")]
    [SerializeField] private AudioClip fireSfx;


    // Animator věže.
    // Například může při výstřelu spustit animaci.
    [Tooltip("Animator věže – pokud existuje, přehraje stav 'Fire' při výstřelu.")]
    [SerializeField] private Animator towerAnimator;


    // Název triggeru v Animatoru, který spustí animaci výstřelu.
    [Tooltip("Jméno Animator triggeru pro výstřel.")]
    [SerializeField] private string fireTriggerName = "Fire";


    // AudioSource použitý pro přehrávání zvuku výstřelu.
    //
    // Věž nemá AudioSource automaticky přes RequireComponent,
    // takže ho musíš přidat na prefab nebo přiřadit v Inspectoru.
    [SerializeField] private AudioSource audioSource;


    // ============================================================
    //  SERVEROVÝ TARGET
    // ============================================================

    /*
     * Aktuálně zamčený cíl na serveru.
     *
     * Server sám rozhoduje, na koho věž míří.
     * Není to NetworkVariable.
     */
    private NetworkObject lockedTarget;


    /*
     * Lokální target na daném klientovi.
     *
     * Server sem má lockedTarget.
     *
     * Klient si sem uloží target, který mu přišel přes ClientRpc.
     *
     * Díky tomu může klient sám lokálně počítat rotaci věže.
     */
    private NetworkObject localTarget;


    // ============================================================
    //  INTERNÍ PROMĚNNÉ
    // ============================================================

    /*
     * Pole colliderů, které naplní Physics.OverlapSphereNonAlloc().
     *
     * 32 znamená, že najednou zpracujeme maximálně 32 nalezených colliderů.
     *
     * Pokud je v dosahu více colliderů než 32, některé se sem nevejdou.
     */
    private readonly Collider[] enemyResults = new Collider[32];


    /*
     * Uchovává čas, kdy se má příště provést skenování okolí.
     */
    private float nextEnemyScan;


    /*
     * Časovač útoku.
     *
     * Postupně roste pomocí Time.deltaTime.
     * Jakmile dosáhne AttackCooldown, věž vystřelí.
     */
    private float attackTimer;


    // ============================================================
    //  POSTAVENÍ VĚŽE
    // ============================================================

    public override void OnBuilt()
    {
        /*
         * Při postavení začneme s nulovým časem útoku.
         */
        attackTimer = 0f;


        /*
         * Pokud máme LineRenderer, laser je na začátku vypnutý.
         *
         * Zapne se pouze na krátkou dobu při výstřelu.
         */
        if (laserBeam != null)
            laserBeam.enabled = false;


        /*
         * Při postavení nemáme zatím žádný lokální target.
         */
        localTarget = null;


        /*
         * Server také začíná bez targetu.
         */
        if (IsServer)
            lockedTarget = null;
    }


    // ============================================================
    //  HLAVNÍ LOGIKA VĚŽE
    // ============================================================

    public override void UpdateBuilding()
    {
        // --------------------------------------------------------
        // SERVEROVÁ ČÁST
        // --------------------------------------------------------
        //
        // Pouze server:
        // - hledá nepřátele
        // - rozhoduje o cíli
        // - rozhoduje, kdy target začíná / končí
        // - provádí útok
        // - způsobuje damage
        //
        // Klienti nic z toho nerozhodují.
        // --------------------------------------------------------

        if (IsServer)
        {
            // Je čas znovu prohledat okolí?
            if (Time.time >= nextEnemyScan)
            {
                // Naplánujeme další skenování.
                nextEnemyScan = Time.time + enemyScanInterval;

                // Ověříme současný cíl nebo najdeme nový.
                ValidateOrAcquireTarget();
            }


            // Pokud máme nějaký zamčený cíl,
            // začneme počítat čas do dalšího výstřelu.
            if (lockedTarget != null)
            {
                attackTimer += Time.deltaTime;


                // Pokud cooldown útoku skončil,
                // věž vystřelí.
                if (attackTimer >= AttackCooldown)
                {
                    // Resetujeme časovač.
                    attackTimer = 0f;

                    // Provedeme útok na současný cíl.
                    FireAt(lockedTarget);
                }


                // Server svou věž otáčí lokálně.
                //
                // Žádná rotace se přes síť neposílá.
                RotateTurretToTarget(lockedTarget);
            }
        }
        else
        {
            // ----------------------------------------------------
            // KLIENTSKÁ ČÁST
            // ----------------------------------------------------
            //
            // Klient pouze používá target,
            // který mu poslal server přes RPC.
            //
            // Samotnou rotaci si počítá sám.
            // ----------------------------------------------------

            if (localTarget != null)
            {
                RotateTurretToTarget(localTarget);
            }
        }
    }


    // ============================================================
    //  ROTACE TURETU NA CÍL
    // ============================================================

    private void RotateTurretToTarget(NetworkObject target)
    {
        /*
         * Pokud není v Inspectoru nastaven turretPivot,
         * nemáme co otáčet.
         */
        if (turretPivot == null)
            return;


        /*
         * Pokud nemáme target,
         * nemáme na co mířit.
         */
        if (target == null)
            return;


        /*
         * Vypočítáme vektor od turretu směrem k cíli.
         */
        Vector3 direction =
            target.transform.position - turretPivot.position;


        /*
         * Nastavíme Y na 0.
         *
         * Tím zabráníme tomu, aby se turret nakláněl nahoru/dolů.
         *
         * Otáčí se tedy pouze horizontálně.
         */
        direction.y = 0f;


        /*
         * Pokud je cíl extrémně blízko středu turretu,
         * směr je prakticky nulový.
         */
        if (direction.sqrMagnitude < 0.001f)
            return;


        /*
         * Vytvoříme rotaci, která míří směrem k cíli.
         */
        Quaternion desiredRotation =
            Quaternion.LookRotation(direction);


        /*
         * Věž otáčíme postupně.
         *
         * Každý klient si tedy tuto rotaci počítá sám.
         */
        turretPivot.rotation = Quaternion.RotateTowards(
            turretPivot.rotation,
            desiredRotation,
            turretRotationSpeed * Time.deltaTime
        );
    }


    // ============================================================
    //  HLEDÁNÍ / OVĚŘOVÁNÍ CÍLE
    // ============================================================

    private void ValidateOrAcquireTarget()
    {
        /*
         * Pokud už nějaký cíl máme,
         * nejdříve zkontrolujeme, jestli je stále platný.
         */
        if (lockedTarget != null &&
            !IsValidEnemyTarget(lockedTarget))
        {
            /*
             * Cíl už není platný.
             *
             * Například mohl:
             * - zemřít
             * - odjet mimo dosah
             * - přestat být nepřítelem
             */

            lockedTarget = null;


            /*
             * Pošleme všem klientům informaci,
             * že věž už žádný target nemá.
             */
            ClearTargetClientRpc();


            /*
             * Resetujeme cooldown.
             */
            attackTimer = 0f;
        }


        /*
         * Pokud máme stále platný cíl,
         * není potřeba hledat nový.
         *
         * Věž zůstává zamčená na současného nepřítele.
         */
        if (lockedTarget != null)
            return;


        /*
         * Nemáme cíl, takže hledáme nejbližšího nepřítele.
         */
        NetworkObject best = FindNearestEnemy();


        /*
         * Pokud jsme nikoho nenašli,
         * nic dalšího neděláme.
         */
        if (best == null)
            return;


        /*
         * Uložíme nalezený objekt jako nový cíl na serveru.
         */
        lockedTarget = best;


        /*
         * Pošleme target klientům.
         *
         * Důležité:
         * neposíláme rotaci.
         *
         * Posíláme pouze:
         *
         * "Tato věž má target X."
         *
         * Každý klient si pak podle skutečné pozice targetu
         * sám spočítá výslednou rotaci.
         */
        SetTargetClientRpc(best);


        /*
         * Když získáme nový cíl,
         * resetujeme cooldown útoku.
         */
        attackTimer = 0f;
    }


    // ============================================================
    //  RPC – NASTAVENÍ TARGETU KLIENTŮM
    // ============================================================

    [ClientRpc]
    private void SetTargetClientRpc(NetworkObjectReference targetReference)
    {
        /*
         * Pokusíme se z NetworkObjectReference získat
         * skutečný lokální NetworkObject.
         */
        if (targetReference.TryGet(out NetworkObject target))
        {
            /*
             * Uložíme target lokálně.
             *
             * Tento target pak klient používá pro výpočet rotace.
             */
            localTarget = target;
        }
        else
        {
            /*
             * Pokud se reference nepodařilo vyřešit,
             * raději target smažeme.
             */
            localTarget = null;
        }
    }


    // ============================================================
    //  RPC – SMAZÁNÍ TARGETU KLIENTŮM
    // ============================================================

    [ClientRpc]
    private void ClearTargetClientRpc()
    {
        /*
         * Klient dostal od serveru informaci:
         *
         * "Tahle věž už na nic nemíří."
         *
         * Proto smažeme jeho lokální target.
         */
        localTarget = null;
    }


    // ============================================================
    //  HLEDÁNÍ NEJBLIŽŠÍHO NEPŘÍTELE
    // ============================================================

    private NetworkObject FindNearestEnemy()
    {
        /*
         * Physics.OverlapSphereNonAlloc hledá všechny collidery
         * uvnitř koule se středem v pozici věže.
         *
         * AttackRange určuje poloměr této koule.
         *
         * enemyLayer určuje, které layery vůbec hledáme.
         */
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            AttackRange,
            enemyResults,
            enemyLayer
        );


        /*
         * Zatím nemáme vybraného žádného nepřítele.
         */
        NetworkObject nearest = null;


        /*
         * Nejmenší nalezená vzdálenost.
         */
        float nearestDistSqr = float.MaxValue;


        // Projdeme všechny collidery, které Physics našel.
        for (int i = 0; i < count; i++)
        {
            /*
             * Pokud je collider null, přeskočíme ho.
             */
            if (enemyResults[i] == null)
                continue;


            /*
             * Collider nemusí být přímo na objektu s NetworkObjectem.
             *
             * Například může být na child objektu jednotky.
             */
            NetworkObject candidate =
                enemyResults[i].GetComponentInParent<NetworkObject>();


            /*
             * Pokud collider nepatří NetworkObjectu,
             * není pro nás použitelným síťovým cílem.
             */
            if (candidate == null)
                continue;


            /*
             * Ověříme, jestli je nalezený objekt skutečně platným
             * nepřítelem.
             */
            if (!IsValidEnemyTarget(candidate))
                continue;


            /*
             * Spočítáme druhou mocninu vzdálenosti.
             */
            float distSqr =
                (candidate.transform.position - transform.position)
                .sqrMagnitude;


            /*
             * Pokud je tento cíl blíž než předchozí nejbližší cíl,
             * nastavíme ho jako nový nejbližší.
             */
            if (distSqr < nearestDistSqr)
            {
                nearestDistSqr = distSqr;
                nearest = candidate;
            }
        }


        /*
         * Vrátíme nejbližšího platného nepřítele.
         */
        return nearest;
    }


    // ============================================================
    //  KONTROLA, JESTLI JE OBJEKT PLATNÝ NEPŘÍTEL
    // ============================================================

    /// <summary>
    /// Kontroluje, jestli je objekt:
    /// - platný
    /// - v dosahu věže
    /// - nepřátelská jednotka nebo budova
    /// - živý
    /// </summary>
    private bool IsValidEnemyTarget(NetworkObject netObj)
    {
        /*
         * Null objekt samozřejmě nemůže být nepřítelem.
         */
        if (netObj == null)
            return false;


        /*
         * Spočítáme vzdálenost mezi věží a objektem.
         */
        float distSqr =
            (netObj.transform.position - transform.position)
            .sqrMagnitude;


        /*
         * Pokud je objekt dál než AttackRange,
         * není platným cílem.
         */
        if (distSqr > AttackRange * AttackRange)
            return false;


        // --------------------------------------------------------
        // KONTROLA, JESTLI JE CÍLEM JEDNOTKA
        // --------------------------------------------------------

        /*
         * Zkusíme zjistit, jestli NetworkObject obsahuje Troop.
         */
        if (netObj.TryGetComponent(out Troop troop))
        {
            /*
             * OwnerClientId říká, kterému klientovi jednotka patří.
             *
             * Pokud je jiný než OwnerClientId věže,
             * považujeme jednotku za nepřátelskou.
             */
            return troop.OwnerClientId != OwnerClientId
                   && troop.Health.Value > 0;
        }


        // --------------------------------------------------------
        // KONTROLA, JESTLI JE CÍLEM BUDOVA
        // --------------------------------------------------------

        /*
         * Pokud to není Troop, zkusíme zjistit, jestli jde o Building.
         */
        if (netObj.TryGetComponent(out Building building))
        {
            /*
             * Stejný princip jako u jednotky:
             *
             * jiný OwnerClientId = nepřítel
             *
             * Health > 0 = budova je živá
             */
            return building.OwnerClientId != OwnerClientId
                   && building.Health.Value > 0;
        }


        /*
         * Pokud objekt není ani Troop ani Building,
         * není pro tuto věž platným cílem.
         */
        return false;
    }


    // ============================================================
    //  VÝSTŘEL
    // ============================================================

    private void FireAt(NetworkObject target)
    {
        /*
         * Bez cíle není na koho vystřelit.
         */
        if (target == null)
            return;


        // --------------------------------------------------------
        // SERVEROVÝ DAMAGE
        // --------------------------------------------------------

        /*
         * Pokud je cíl jednotka,
         * způsobíme jí damage.
         */
        if (target.TryGetComponent(out Troop enemyTroop))
        {
            enemyTroop.TakeDamage(Damage);
        }


        /*
         * Pokud cíl není jednotka, ale je budova,
         * způsobíme damage budově.
         */
        else if (target.TryGetComponent(out Building enemyBuilding))
        {
            enemyBuilding.TakeDamage(Damage);
        }


        // --------------------------------------------------------
        // VIZUÁLNÍ A ZVUKOVÝ EFEKT
        // --------------------------------------------------------

        /*
         * Pošleme všem klientům informaci:
         * "Laser právě vystřelil na tuto pozici."
         *
         * Samotný damage už byl proveden serverem výše.
         */
        PlayFireEffectClientRpc(target.transform.position);
    }


    // ============================================================
    //  VIZUÁLNÍ EFEKT VÝSTŘELU
    // ============================================================

    [ClientRpc]
    private void PlayFireEffectClientRpc(Vector3 targetPosition)
    {
        // --------------------------------------------------------
        // LASER BEAM
        // --------------------------------------------------------

        if (laserBeam != null && firePoint != null)
        {
            /*
             * Začátek laseru nastavíme na firePoint.
             */
            laserBeam.SetPosition(0, firePoint.position);


            /*
             * Konec laseru nastavíme na pozici cíle.
             */
            laserBeam.SetPosition(1, targetPosition);


            /*
             * Laser zapneme.
             */
            laserBeam.enabled = true;


            /*
             * Po krátké době ho vypneme.
             */
            StartCoroutine(HideLaserAfterDelay());
        }


        // --------------------------------------------------------
        // PARTICLE EFEKT
        // --------------------------------------------------------

        if (shotParticle != null && firePoint != null)
        {
            /*
             * Spočítáme směr od firePointu k cíli.
             */
            Vector3 direction =
                targetPosition - firePoint.position;


            /*
             * Pokud má směr dostatečnou délku,
             * otočíme particle efekt směrem k cíli.
             */
            if (direction.sqrMagnitude > 0.0001f)
            {
                shotParticle.transform.rotation =
                    Quaternion.LookRotation(direction.normalized);
            }


            /*
             * Spustíme particle efekt.
             */
            shotParticle.Play();
        }


        // --------------------------------------------------------
        // ANIMACE
        // --------------------------------------------------------

        if (towerAnimator != null &&
            !string.IsNullOrEmpty(fireTriggerName))
        {
            /*
             * Aktivujeme trigger v Animatoru.
             */
            towerAnimator.SetTrigger(fireTriggerName);
        }


        // --------------------------------------------------------
        // ZVUK
        // --------------------------------------------------------

        if (audioSource != null && fireSfx != null)
        {
            /*
             * Přehrajeme zvuk výstřelu.
             */
            audioSource.PlayOneShot(fireSfx);
        }
    }


    // ============================================================
    //  SKRYTÍ LASERU PO VÝSTŘELU
    // ============================================================

    private IEnumerator HideLaserAfterDelay()
    {
        /*
         * Počkáme dobu nastavenou v Inspectoru.
         */
        yield return new WaitForSeconds(laserDisplayDuration);


        /*
         * Laser zase vypneme.
         */
        if (laserBeam != null)
            laserBeam.enabled = false;
    }


    // ============================================================
    //  GIZMO PRO UNITY EDITOR
    // ============================================================

#if UNITY_EDITOR

    private void OnDrawGizmosSelected()
    {
        /*
         * Když vybereš LaserTower v Unity Editoru,
         * zobrazí se kolem ní červený kruh/koule představující
         * její AttackRange.
         *
         * Toto je pouze editorová pomůcka.
         * Nemá žádný vliv na samotnou hru.
         */
        Gizmos.color = Color.red;

        Gizmos.DrawWireSphere(
            transform.position,
            AttackRange
        );
    }

#endif
}