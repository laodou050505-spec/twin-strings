"""金腰带金牌的 Blender 修整（在 Blender 里运行）：
  Blender -b -P Tools/belt_trim.py -- <lod0.fbx> [lod1.fbx ...]

P2 生成的牌子正面是浮雕，背面常常带一个“展示支架”或凹坑（参考图里看不到背面，模型自己编的）。
腰带上牌子是贴在皮带上的，背面看不到，支架却会从皮带后面戳出来。这个脚本：
  1. 找出牌子最薄的那个方向（厚度方向）和哪一面是正面（浮雕那面顶点多得多）；
  2. 用外圈（离中轴最远的那一圈顶点）量出牌身背面在哪；
  3. 沿牌身背面切一刀，把后面的东西全部去掉，切口封成平底；
  4. 原文件备份成 .orig.fbx，修整后的覆盖原名（导出参数和 p2_pipeline 一致）。
"""
import bpy, bmesh, sys, os, shutil
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
for path in argv:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    me = obj.data
    cos = [v.co.copy() for v in me.vertices]
    mn = Vector((min(c[i] for c in cos) for i in range(3)))
    mx = Vector((max(c[i] for c in cos) for i in range(3)))
    size = mx - mn
    ctr = (mn + mx) / 2
    ax = min(range(3), key=lambda i: size[i])            # 厚度方向
    o1, o2 = [i for i in range(3) if i != ax]
    plus = sum(1 for c in cos if c[ax] > ctr[ax])
    sign = 1.0 if plus > len(cos) - plus else -1.0       # 正面朝哪边（浮雕那面顶点多）
    rad = [((c[o1] - ctr[o1]) ** 2 + (c[o2] - ctr[o2]) ** 2) ** 0.5 for c in cos]
    rmax = max(rad)
    rim = [sign * c[ax] for c, r in zip(cos, rad) if r >= 0.93 * rmax]
    back = min(rim)                                       # 牌身背面（按“正面为正”的深度）
    front = max(sign * c[ax] for c in cos)
    cut = back + 0.04 * (front - back)                    # 往里一点点切，保证切到的是实心牌身
    co = Vector((0, 0, 0)); co[ax] = sign * cut
    co[o1] = ctr[o1]; co[o2] = ctr[o2]
    no = Vector((0, 0, 0)); no[ax] = -sign                # 法线指向背面：去掉法线这一侧
    bm = bmesh.new(); bm.from_mesh(me)
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_outer=True, clear_inner=False)
    cut_edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge)]
    before = len(bm.faces)
    if cut_edges:
        bmesh.ops.holes_fill(bm, edges=cut_edges, sides=0)
        open_edges = [e for e in bm.edges if e.is_boundary]
        if open_edges:
            try:
                bmesh.ops.triangle_fill(bm, use_beauty=True, edges=open_edges)
            except Exception as ex:
                print('triangle_fill 失败', ex)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    boundary = sum(1 for e in bm.edges if e.is_boundary)
    bm.to_mesh(me); bm.free(); me.update()
    orig = path[:-4] + '.orig.fbx'
    if not os.path.exists(orig):
        shutil.copy(path, orig)
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, path_mode='STRIP', embed_textures=False,
                             mesh_smooth_type='OFF', apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=True, use_mesh_modifiers=True, add_leaf_bones=False)
    print('TRIM', os.path.basename(path), '厚度轴', 'xyz'[ax], '正面', '+' if sign > 0 else '-',
          '总厚', round(size[ax], 4), '保留厚', round(front - cut, 4), '面数', len(me.polygons), '封口后开放边', boundary, flush=True)
