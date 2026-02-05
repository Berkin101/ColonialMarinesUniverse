using Content.Shared._RMC14.Barricade.Components;
using Content.Shared._RMC14.Construction;
using Content.Shared.Damage;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using static Content.Shared.Physics.CollisionGroup;

namespace Content.Shared._RMC14.Entrenching;

public sealed class BarricadeSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IMapManager _mapManager = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly RMCConstructionSystem _rmcConstruction = default!;
    [Dependency] private readonly SharedStackSystem _stack = default!;
    [Dependency] private readonly ITileDefinitionManager _tiles = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly UseDelaySystem _useDelay = default!;

    private EntityQuery<BarricadeComponent> _barricadeQuery;

    public override void Initialize()
    {
        _barricadeQuery = GetEntityQuery<BarricadeComponent>();

        SubscribeLocalEvent<EntrenchingToolComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<EntrenchingToolComponent, EntrenchingToolDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<EntrenchingToolComponent, ItemToggledEvent>(OnItemToggled);
        SubscribeLocalEvent<EntrenchingToolComponent, SandbagFillDoAfterEvent>(OnSandbagFillDoAfter);
        SubscribeLocalEvent<EntrenchingToolComponent, SandbagDismantleDoAfterEvent>(OnSandbagDismantleDoAfter);
     //testing
        SubscribeLocalEvent<EntrenchingToolComponent, DirtPileDismantleDoAfterEvent>(OnDirtPileDismantleDoAfter);


        SubscribeLocalEvent<EmptySandbagComponent, InteractUsingEvent>(OnEmptyInteractUsing);

        SubscribeLocalEvent<FullSandbagComponent, ActivateInWorldEvent>(OnFullActivateInWorld);
        SubscribeLocalEvent<FullSandbagComponent, AfterInteractEvent>(OnFullAfterInteract);
        SubscribeLocalEvent<FullSandbagComponent, SandbagBuildDoAfterEvent>(OnFullBuildDoAfter);

        // testing
        SubscribeLocalEvent<dirtPileItemComponent, ActivateInWorldEvent>(OnDirtPileActivateInWorld);
        SubscribeLocalEvent<dirtPileItemComponent, AfterInteractEvent>(OnDirtPileAfterInteract);
        SubscribeLocalEvent<dirtPileItemComponent, DirtPileBuildDoAfterEvent>(OnDirtPileBuildDoAfter);

    }

    private void OnAfterInteract(Entity<EntrenchingToolComponent> tool, ref AfterInteractEvent args)
    {
        if (!args.CanReach)
            return;

        if (HasComp<BarricadeSandbagComponent>(args.Target))
        {
            DismantleSandbagBaricade(tool, ref args);
            args.Handled = true;
            return;
        }

        if (HasComp<BarricadeDirtComponent>(args.Target))
        {
            DismantleDirtBaricade(tool, ref args);
            args.Handled = true;
            return;
        }


        StartDigging(tool, args.User, args.ClickLocation);
        args.Handled = true;
    }



    private void DismantleSandbagBaricade(Entity<EntrenchingToolComponent> tool, ref AfterInteractEvent args)
    {
        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return;

        _popup.PopupClient(Loc.GetString("cm-entrenching-dismantle"), args.User, args.User);

        var ev = new SandbagDismantleDoAfterEvent(GetNetCoordinates(args.ClickLocation));
        var doAfter = new DoAfterArgs(EntityManager, args.User, tool.Comp.DigDelay, ev, tool, args.Target, tool)
        {
            BreakOnMove = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }



    private void DismantleDirtBaricade(Entity<EntrenchingToolComponent> tool, ref AfterInteractEvent args)
    {
        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return;

        _popup.PopupClient(Loc.GetString("cm-entrenching-dismantle"), args.User, args.User);

        var ev = new DirtPileDismantleDoAfterEvent(GetNetCoordinates(args.ClickLocation));
        var doAfter = new DoAfterArgs(EntityManager, args.User, tool.Comp.DigDelay, ev, tool, args.Target, tool)
        {
            BreakOnMove = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }


    private void OnSandbagDismantleDoAfter(Entity<EntrenchingToolComponent> tool, ref SandbagDismantleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        if (!TryComp(args.Target, out BarricadeSandbagComponent? barricade))
            return;
        var full = Spawn(barricade.Material, GetCoordinates(args.Coordinates));

        var bagsSalvaged = barricade.MaxMaterial;
        if (bagsSalvaged <= 0 && TryComp(full, out FullSandbagComponent? fullSandbag))
            bagsSalvaged = fullSandbag.StackRequired;
        if (TryComp(args.Target, out DamageableComponent? damageable))
            bagsSalvaged -= Math.Max((int) damageable.TotalDamage / barricade.MaterialLossDamageInterval - 1, 0);

        if (TryComp(args.Target, out BarbedComponent? barbed) && barbed.IsBarbed)
            Spawn(barbed.Spawn, GetCoordinates(args.Coordinates));

        Del(args.Target);

        if (bagsSalvaged <= 0)
        {
            Del(full);
            return;
        }

        if (TryComp(full, out StackComponent? fullStack))
            _stack.SetCount(full, bagsSalvaged, fullStack);
    }

    // may not be optimized!!!
    private void OnDirtPileDismantleDoAfter(Entity<EntrenchingToolComponent> tool, ref DirtPileDismantleDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (_net.IsClient)
            return;

        if (!TryComp(args.Target, out BarricadeDirtComponent? barricade))
            return;
        var full = Spawn(barricade.Material, GetCoordinates(args.Coordinates));

        var dirtSalvaged = barricade.MaxMaterial;
        if (dirtSalvaged <= 0 && TryComp(full, out dirtPileItemComponent? dirtPile))
            dirtSalvaged = dirtPile.StackRequired;
        if (TryComp(args.Target, out DamageableComponent? damageable))
            dirtSalvaged -= Math.Max((int) damageable.TotalDamage / barricade.MaterialLossDamageInterval - 1, 0);

        Del(args.Target);

        if (dirtSalvaged <= 0)
        {
            Del(full);
            return;
        }

        if (TryComp(full, out StackComponent? fullStack))
            _stack.SetCount(full, dirtSalvaged, fullStack);
    }




    private void OnDoAfter(Entity<EntrenchingToolComponent> tool, ref EntrenchingToolDoAfterEvent args)
    {
        if (args.Handled)
            return;

        if (args.Cancelled)
        {
            _popup.PopupClient(Loc.GetString("cm-entrenching-stop-digging"), args.User, args.User);
            return;
        }

        var coordinates = GetCoordinates(args.Coordinates);
        if (!CanDig(tool, args.User, coordinates, false, out _, out _))
            return;

        args.Handled = true;
        tool.Comp.TotalLayers = tool.Comp.LayersPerDig;
        Dirty(tool);

        var userCoordinates = _transform.GetMoverCoordinates(args.User);
        var emptyNearby = _lookup.GetEntitiesInRange<EmptySandbagComponent>(userCoordinates, 1.5f);
        foreach (var empty in emptyNearby)
        {
            var ev = new SandbagFillDoAfterEvent();
            var doAfter = new DoAfterArgs(EntityManager, args.User, tool.Comp.FillDelay, ev, tool, empty, tool)
            {
                BreakOnMove = true,
            };
            _doAfter.TryStartDoAfter(doAfter);
            _popup.PopupClient(Loc.GetString("cm-entrenching-begin-filling"), args.User, args.User);
            break;
        }
    }

    private void OnItemToggled(Entity<EntrenchingToolComponent> tool, ref ItemToggledEvent args)
    {
        tool.Comp.TotalLayers = 0;
        Dirty(tool);
    }

    private void OnSandbagFillDoAfter(Entity<EntrenchingToolComponent> tool, ref SandbagFillDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;
        var userCoordinates = _transform.GetMoverCoordinates(args.User);
        var emptyNearby = _lookup.GetEntitiesInRange<EmptySandbagComponent>(userCoordinates, 1.5f);
        var filled = false;
        foreach (var empty in emptyNearby)
        {
            if (filled)
            {
                args.Repeat = true;
                break;
            }

            filled = true;
            Fill(tool, empty, args.User, tool.Comp.TotalLayers);
            if (!TerminatingOrDeleted(empty))
            {
                args.Repeat = true;
                break;
            }
        }

        if (tool.Comp.TotalLayers <= 0)
        {
            args.Repeat = false;
            StartDigging(tool, args.User, tool.Comp.LastDigLocation);
        }
    }

    private void OnEmptyInteractUsing(Entity<EmptySandbagComponent> empty, ref InteractUsingEvent args)
    {
        if (_net.IsClient || args.Handled)
            return;

        if (!TryComp(args.Used, out EntrenchingToolComponent? toolComp))
            return;

        args.Handled = true;

        var tool = new Entity<EntrenchingToolComponent>(args.Used, toolComp);
        var ev = new SandbagFillDoAfterEvent();
        var doAfter = new DoAfterArgs(EntityManager, args.User, tool.Comp.FillDelay, ev, tool, empty, tool)
        {
            BreakOnMove = true,
        };
        _doAfter.TryStartDoAfter(doAfter);
        _popup.PopupClient(Loc.GetString("cm-entrenching-begin-filling"), args.User, args.User);
    }

    private void OnFullActivateInWorld(Entity<FullSandbagComponent> full, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp(args.User, out TransformComponent? transform))
            return;

        var coordinates = _transform.GetMoverCoordinates(args.User, transform);
        var direction = transform.LocalRotation.GetCardinalDir();
        if (Build(full, args.User, coordinates, direction, out var handled))
            args.Handled = handled;
    }

    private void OnDirtPileActivateInWorld(Entity<dirtPileItemComponent> item, ref ActivateInWorldEvent args)
    {
        if (args.Handled || !TryComp(args.User, out TransformComponent? transform))
            return;

        var coordinates = _transform.GetMoverCoordinates(args.User, transform);
        var direction = transform.LocalRotation.GetCardinalDir();
        if (BuildDirtMound(item, args.User, coordinates, direction, out var handled))
            args.Handled = handled;
    }



    private void OnFullAfterInteract(Entity<FullSandbagComponent> full, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp(args.User, out TransformComponent? transform))
            return;

        var direction = transform.LocalRotation.GetCardinalDir();
        if (Build(full, args.User, args.ClickLocation, direction, out var handled))
            args.Handled = handled;
    }



    private void OnDirtPileAfterInteract(Entity<dirtPileItemComponent> item, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp(args.User, out TransformComponent? transform))
            return;

        var direction = transform.LocalRotation.GetCardinalDir();
        if (BuildDirtMound(item, args.User, args.ClickLocation, direction, out var handled))
            args.Handled = handled;
    }



    private void OnFullBuildDoAfter(Entity<FullSandbagComponent> full, ref SandbagBuildDoAfterEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Cancelled || args.Handled)
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!_mapManager.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_interaction.InRangeUnobstructed(full, coordinates, popup: false) ||
            !_turf.TryGetTileRef(coordinates, out var turf) ||
            !CanBuild(full, (gridId, gridComp), args.User, turf.Value, args.Direction))
        {
            return;
        }

        if (full.Comp.StackRequired > 1)
        {
            var count = _stack.GetCount(full);
            if (count < full.Comp.StackRequired)
                return;

            if (TryComp(full, out StackComponent? fullStack))
                _stack.SetCount(full, count - full.Comp.StackRequired, fullStack);
            else
                QueueDel(full);
        }

        var built = SpawnAtPosition(full.Comp.Builds, coordinates);
        _transform.SetLocalRotation(built, args.Direction.ToAngle());

        args.Handled = true;
    }

// Optimizable maybe?
    private void OnDirtPileBuildDoAfter(Entity<dirtPileItemComponent> item, ref DirtPileBuildDoAfterEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Cancelled || args.Handled)
            return;

        var coordinates = GetCoordinates(args.Coordinates);
        if (!_mapManager.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_interaction.InRangeUnobstructed(item, coordinates, popup: false) ||
            !_turf.TryGetTileRef(coordinates, out var turf) ||
            !CanBuildDirtMound(item, (gridId, gridComp), args.User, turf.Value, args.Direction))
        {
            return;
        }

        if (item.Comp.StackRequired > 0) // >0, instead of >=1 (less checks)
        {
            var count = _stack.GetCount(item);
            if (count < item.Comp.StackRequired)
                return;

            if (TryComp(item, out StackComponent? fullStack))
                _stack.SetCount(item, count - item.Comp.StackRequired, fullStack);
            else
                QueueDel(item);
        }

        var built = SpawnAtPosition(item.Comp.Builds, coordinates);
        _transform.SetLocalRotation(built, args.Direction.ToAngle());

        args.Handled = true;
    }



    private bool StartDigging(Entity<EntrenchingToolComponent> tool, EntityUid user, EntityCoordinates clicked)
    {
        if (!CanDig(tool, user, clicked, true, out var grid, out var tile))
            return false;

        var coordinates = _mapSystem.GridTileToLocal(grid, grid, tile.GridIndices);
        tool.Comp.LastDigLocation = coordinates;
        Dirty(tool);

        var ev = new EntrenchingToolDoAfterEvent(GetNetCoordinates(coordinates));
        var doAfter = new DoAfterArgs(EntityManager, user, tool.Comp.DigDelay, ev, tool, used: tool)
        {
            BreakOnMove = true,
            NeedHand = true,
            BreakOnHandChange = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        _popup.PopupClient(Loc.GetString("cm-entrenching-start-digging"), user, user);
        _audio.PlayPredicted(tool.Comp.DigSound, user, user);

        if (TryComp(tool, out UseDelayComponent? useDelay))
            _useDelay.TryResetDelay((tool, useDelay));

        return true;
    }

    private bool Fill(Entity<EntrenchingToolComponent> tool, Entity<EmptySandbagComponent> empty, EntityUid user, int amount)
    {
        if (tool.Comp.TotalLayers < amount)
            return false;

        var toRemove = amount;
        var coordinates = _transform.GetMoverCoordinates(empty);

        if (TryComp(empty, out StackComponent? stack))
        {
            var stackCount = _stack.GetCount(empty, stack);
            toRemove = Math.Min(toRemove, stackCount);
            _stack.SetCount(empty, stackCount - toRemove, stack);

            if (_net.IsServer)
            {
                var filled = Spawn(empty.Comp.Filled, coordinates);
                var filledStack = EnsureComp<StackComponent>(filled);
                _stack.SetCount(filled, toRemove, filledStack);
            }
        }
        else
        {
            if (_net.IsServer)
                Del(empty);
        }

        tool.Comp.TotalLayers -= toRemove;
        Dirty(tool);
        _audio.PlayPredicted(tool.Comp.FillSound, user, user);

        return true;
    }

    private bool Build(Entity<FullSandbagComponent> full, EntityUid user, EntityCoordinates coordinates, Direction direction, out bool handled)
    {
        handled = false;
        if (!_mapManager.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_turf.TryGetTileRef(coordinates, out var tile))
        {
            return false;
        }

        handled = true;
        if (!CanBuild(full, (gridId, gridComp), user, tile.Value, direction))
            return false;

        var ev = new SandbagBuildDoAfterEvent(GetNetCoordinates(coordinates), direction);
        var doAfter = new DoAfterArgs(EntityManager, user, full.Comp.BuildDelay, ev, full, full)
        {
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        return true;
    }

 // Optimizable maybe?
    private bool BuildDirtMound(Entity<dirtPileItemComponent> item, EntityUid user, EntityCoordinates coordinates, Direction direction, out bool handled)
    {
        handled = false;
        if (!_mapManager.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp) ||
            !_turf.TryGetTileRef(coordinates, out var tile))
        {
            return false;
        }

        handled = true;
        if (!CanBuildDirtMound(item, (gridId, gridComp), user, tile.Value, direction))
            return false;

        var ev = new DirtPileBuildDoAfterEvent(GetNetCoordinates(coordinates), direction);
        var doAfter = new DoAfterArgs(EntityManager, user, item.Comp.BuildDelay, ev, item, item)
        {
            BreakOnMove = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
        return true;
    }

    private bool CanDig(
        Entity<EntrenchingToolComponent> tool,
        EntityUid user,
        EntityCoordinates coordinates,
        bool checkUseDelay,
        out Entity<MapGridComponent> grid,
        out TileRef tileRef)
    {
        grid = default;
        tileRef = default;

        if (checkUseDelay &&
            TryComp(tool, out UseDelayComponent? useDelay) &&
            _useDelay.IsDelayed((tool, useDelay)))
        {
            return false;
        }

        if (!_interaction.InRangeUnobstructed(user, coordinates, popup: false))
            return false;

        if (TryComp(tool, out ItemToggleComponent? toggle) && !toggle.Activated)
            return false;

        if (!_mapManager.TryFindGridAt(_transform.ToMapCoordinates(coordinates), out var gridId, out var gridComp))
            return false;

        tileRef = _mapSystem.GetTileRef(gridId, gridComp, coordinates);
        var tileDef = (ContentTileDefinition) _tiles[tileRef.Tile.TypeId];
        if (!tileDef.CanDig)
            return false;

        if (!TileSolidAndNotBlocked(tileRef))
            return false;

        grid = (gridId, gridComp);
        return true;
    }

    private bool TileSolidAndNotBlocked(TileRef tile)
    {
        return !_turf.IsSpace(tile) &&
               _turf.GetContentTileDefinition(tile).Sturdy &&
               !_turf.IsTileBlocked(tile, Impassable);
    }

    private bool CanBuild(
        Entity<FullSandbagComponent> full,
        Entity<MapGridComponent> grid,
        EntityUid user,
        TileRef tile,
        Direction direction)
    {
        if (!TryComp(full, out StackComponent? stack) ||
            stack.Count < 5)
        {
            return false;
        }

        var coordinates = new EntityCoordinates(tile.GridUid, tile.X, tile.Y).Offset(grid.Comp.TileSizeHalfVector);
        var mask = Impassable | InteractImpassable | TableLayer;
        var popup = _net.IsClient;
        if (!_interaction.InRangeUnobstructed(user, coordinates, collisionMask: mask, popup: popup))
            return false;

        if (!TileSolidAndNotBlocked(tile))
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(grid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<BarricadeComponent>(uid) &&
                TryComp(uid, out TransformComponent? transform) &&
                transform.LocalRotation.GetCardinalDir() == direction)
            {
                return false;
            }
        }

        var name = _prototype.TryIndex(full.Comp.Builds, out var builds) ? builds.Name : Name(full);
        if (!_rmcConstruction.CanBuildAt(coordinates, name, out var popupStr))
        {
            if (popup)
                _popup.PopupClient(popupStr, user, user, PopupType.SmallCaution);

            return false;
        }

        return true;
    }

 // likely unoptimissed due to lack of my C# skill (dont know how to integrate it to the one above so I made my own func)
    private bool CanBuildDirtMound(
        Entity<dirtPileItemComponent> item,
        Entity<MapGridComponent> grid,
        EntityUid user,
        TileRef tile,
        Direction direction)
    {
        if (!TryComp(item, out StackComponent? stack) ||
            stack.Count < 1)
        {
            return false;
        }

        var coordinates = new EntityCoordinates(tile.GridUid, tile.X, tile.Y).Offset(grid.Comp.TileSizeHalfVector);
        var mask = Impassable | InteractImpassable | TableLayer;
        var popup = _net.IsClient;
        if (!_interaction.InRangeUnobstructed(user, coordinates, collisionMask: mask, popup: popup))
            return false;

        if (!TileSolidAndNotBlocked(tile))
            return false;

        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(grid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var uid))
        {
            if (HasComp<BarricadeComponent>(uid) &&
                TryComp(uid, out TransformComponent? transform) &&
                transform.LocalRotation.GetCardinalDir() == direction)
            {
                return false;
            }
        }

        var name = _prototype.TryIndex(item.Comp.Builds, out var builds) ? builds.Name : Name(item);
        if (!_rmcConstruction.CanBuildAt(coordinates, name, out var popupStr))
        {
            if (popup)
                _popup.PopupClient(popupStr, user, user, PopupType.SmallCaution);

            return false;
        }

        return true;
    }



    public bool HasBarricadeFacing(EntityCoordinates coordinates, Direction direction)
    {
        if (_transform.GetGrid(coordinates) is not { } gridId ||
            !TryComp(gridId, out MapGridComponent? grid))
        {
            return false;
        }

        var indices = _mapSystem.TileIndicesFor(gridId, grid, coordinates);
        var anchored = _mapSystem.GetAnchoredEntitiesEnumerator(gridId, grid, indices);
        while (anchored.MoveNext(out var uid))
        {
            if (_barricadeQuery.HasComp(uid))
            {
                var barricadeDir = _transform.GetWorldRotation(uid.Value).GetCardinalDir();
                if (barricadeDir == direction)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
