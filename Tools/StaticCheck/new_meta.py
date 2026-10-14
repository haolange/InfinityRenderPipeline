#!/usr/bin/env python3
"""Create Unity .meta files (fresh GUIDs) for files and folders that lack one.

Usage: new_meta.py <path> [<path> ...]   (paths may be files or folders; folders are walked)
Never overwrites an existing .meta.
"""
import os, sys, uuid

TAIL = "  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n"
IMPORTERS = {
    '.cs': "MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    '.compute': "ComputeShaderImporter:\n  externalObjects: {}\n  currentAPIMask: 0\n  preprocessorOverride: 0\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    '.hlsl': "ShaderIncludeImporter:\n" + TAIL,
    '.shader': "ShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n",
    '.md': "TextScriptImporter:\n" + TAIL,
    '.txt': "TextScriptImporter:\n" + TAIL,
    '.json': "TextScriptImporter:\n" + TAIL,
    '.asmdef': "AssemblyDefinitionImporter:\n" + TAIL,
    '.asmref': "AssemblyDefinitionReferenceImporter:\n" + TAIL,
    '.uss': "ScriptedImporter:\n  internalIDToNameTable: []\n  externalObjects: {}\n  serializedVersion: 2\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n  script: {fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}\n  disableValidation: 0\n",
}

def write_meta(path):
    meta = path + '.meta'
    if os.path.exists(meta) or path.endswith('.meta') or os.path.basename(path).startswith('.'):
        return False
    guid = uuid.uuid4().hex
    if os.path.isdir(path):
        body = "folderAsset: yes\nDefaultImporter:\n" + TAIL
    else:
        body = IMPORTERS.get(os.path.splitext(path)[1], "DefaultImporter:\n" + TAIL)
    with open(meta, 'w', newline='\n') as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\n{body}")
    print(f"meta: {meta}")
    return True

def main():
    for root in sys.argv[1:]:
        root = root.rstrip('/')
        write_meta(root)
        if os.path.isdir(root):
            for dp, dns, fns in os.walk(root):
                dns[:] = [d for d in dns if not d.startswith('.')]
                for n in dns + fns:
                    write_meta(os.path.join(dp, n))

if __name__ == '__main__':
    main()
