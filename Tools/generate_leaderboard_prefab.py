import re
import random
import os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
main = os.path.join(ROOT, "Assets", "Scenes", "main menu.unity")
text = open(main, encoding="utf-8").read()

blocks = {}
for m in re.finditer(r"^--- !u!(\d+) &(\d+)\n(.*?)(?=^--- !u!|\Z)", text, re.M | re.S):
    blocks[m.group(2)] = {"type": m.group(1), "body": m.group(3)}

transform_ids = {fid for fid, b in blocks.items() if b["type"] == "224"}
children = {tid: [] for tid in transform_ids}
for tid in transform_ids:
    cm = re.search(r"m_Children:\n((?:  - \{fileID: \d+\}\n?)*)", blocks[tid]["body"])
    if cm:
        children[tid] = re.findall(r"\{fileID: (\d+)\}", cm.group(1))

panel_id = next(fid for fid, b in blocks.items() if b["type"] == "1" and "m_Name: LeaderboardPanel" in b["body"])
panel_tr = next(fid for fid, b in blocks.items() if b["type"] == "224" and f"m_GameObject: {{fileID: {panel_id}}}" in b["body"])

# Only objects under panel transform tree
needed = set()
stack = [panel_tr]
while stack:
    t = stack.pop()
    if t in needed:
        continue
    needed.add(t)
    stack.extend(children.get(t, []))

for tid in list(needed):
    needed.add(re.search(r"m_GameObject: \{fileID: (\d+)\}", blocks[tid]["body"]).group(1))

# Tüm bileşenler (Image, CanvasRenderer, Button, TMP, ...)
for fid, b in blocks.items():
    if b["type"] in ("114", "222", "225", "4"):
        gm = re.search(r"m_GameObject: \{fileID: (\d+)\}", b["body"])
        if gm and gm.group(1) in needed:
            needed.add(fid)

mgr_id = next((fid for fid, b in blocks.items() if b["type"] == "114" and "LeaderboardManager" in b["body"]), None)
mgr_body = blocks[mgr_id]["body"] if mgr_id else ""

id_map = {fid: str(random.randint(100000000, 999999999)) for fid in needed}
panel_new = "900000001"
panel_tr_new = "900000002"
mgr_new = "900000003"
ctrl_new = "900000004"
id_map[panel_id] = panel_new
id_map[panel_tr] = panel_tr_new


def remap(body):
    out = body
    for old, new in sorted(id_map.items(), key=lambda x: -len(x[0])):
        out = out.replace(f"&{old}", f"&{new}")
        out = out.replace(f"{{fileID: {old}}}", f"{{fileID: {new}}}")
    return out


lb_panel_ctrl_guid = re.search(
    r"guid: (\w+)",
    open(os.path.join(ROOT, "Assets", "Scripts", "LeaderboardPanelController.cs.meta"), encoding="utf-8").read(),
).group(1)

ordered = sorted(needed, key=lambda x: (blocks[x]["type"], int(x)))
out_blocks = []
for fid in ordered:
    b = blocks[fid]
    body = remap(b["body"])
    if fid == panel_tr:
        body = re.sub(r"m_Father: \{fileID: \d+\}", "m_Father: {fileID: 0}", body)
    out_blocks.append(f"--- !u!{b['type']} &{id_map[fid]}\n{body}")

# Patch panel GameObject components: rect, renderer, image, manager, controller
for i, blk in enumerate(out_blocks):
    if f"&{panel_new}\nGameObject:" in blk:
        comps = re.findall(r"- component: \{fileID: (\d+)\}", blk)
        # RectTransform + CanvasRenderer + Image (ilk 3 UI kök bileşeni)
        rect_id = id_map[panel_tr]
        others = [c for c in comps if c != rect_id][:2]
        base = [rect_id] + others
        new_list = "\n".join(f"  - component: {{fileID: {c}}}" for c in base)
        if mgr_body:
            new_list += f"\n  - component: {{fileID: {mgr_new}}}"
        new_list += f"\n  - component: {{fileID: {ctrl_new}}}"
        blk = re.sub(r"  m_Component:\n(?:  - component: \{fileID: \d+\}\n)+", "  m_Component:\n" + new_list + "\n", blk)
        blk = blk.replace("m_IsActive: 1", "m_IsActive: 0", 1)
        out_blocks[i] = blk
        break

if mgr_body:
    mgr_remapped = remap(mgr_body)
    mgr_remapped = re.sub(
        r"m_GameObject: \{fileID: \d+\}",
        f"m_GameObject: {{fileID: {panel_new}}}",
        mgr_remapped,
    )
    out_blocks.append(f"--- !u!114 &{mgr_new}\n{mgr_remapped}")

out_blocks.append(
    f"""--- !u!114 &{ctrl_new}
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {{fileID: 0}}
  m_PrefabInstance: {{fileID: 0}}
  m_PrefabAsset: {{fileID: 0}}
  m_GameObject: {{fileID: {panel_new}}}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {{fileID: 11500000, guid: {lb_panel_ctrl_guid}, type: 3}}
  m_Name: 
  m_EditorClassIdentifier: Assembly-CSharp::LeaderboardPanelController
  panelRoot: {{fileID: 0}}
"""
)

for i, blk in enumerate(out_blocks):
    if "m_MethodName: CloseLeaderboard" in blk:
        blk = blk.replace("MainMenuManager, Assembly-CSharp", "LeaderboardPanelController, Assembly-CSharp")
        blk = blk.replace("m_MethodName: CloseLeaderboard", "m_MethodName: CloseLeaderboardPanel")
        blk = re.sub(r"m_Target: \{fileID: \d+\}", f"m_Target: {{fileID: {ctrl_new}}}", blk)
        out_blocks[i] = blk

prefab_path = os.path.join(ROOT, "Assets", "Prefabs", "LeaderboardPanel.prefab")
open(prefab_path, "w", encoding="utf-8", newline="\n").write("\n".join(out_blocks) + "\n")
print("Wrote", prefab_path, "with", len(out_blocks), "blocks")
