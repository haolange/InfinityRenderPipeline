#!/usr/bin/env python3
"""Fails when a package assembly the Editor built is older than the newest source file it compiles.

A refresh can report a ready Editor while Library/ScriptAssemblies still holds a previous build (for example after a
compilation that was interrupted). Usage: VerifyScriptAssemblies.py <unity project> <package root>
"""
import json
import os
import sys


def skipped(directory):
    return directory.startswith('.') or directory.endswith('~')


def main(project, package):
    package = os.path.abspath(package)
    owners = {}
    for root, dirs, files in os.walk(package):
        dirs[:] = [d for d in dirs if not skipped(d)]
        for name in files:
            if name.endswith('.asmdef'):
                with open(os.path.join(root, name), encoding='utf-8-sig') as handle:
                    owners[root] = json.load(handle)['name']
    newest = {}
    for root, dirs, files in os.walk(package):
        dirs[:] = [d for d in dirs if not skipped(d)]
        owner = root
        while owner not in owners and len(owner) > len(package):
            owner = os.path.dirname(owner)
        if owner not in owners:
            continue
        assembly = owners[owner]
        for name in files:
            if name.endswith('.cs'):
                newest[assembly] = max(newest.get(assembly, 0.0), os.path.getmtime(os.path.join(root, name)))
    stale = []
    for assembly, source_time in sorted(newest.items()):
        dll = os.path.join(project, 'Library', 'ScriptAssemblies', assembly + '.dll')
        if not os.path.exists(dll):
            # An assembly behind a define constraint (Cli without INFINITY_UNITY_CLI) is not built.
            print(f'ASSEMBLY_NOT_BUILT {assembly}')
            continue
        print(f'ASSEMBLY {assembly} {os.path.getsize(dll)} bytes')
        if os.path.getmtime(dll) < source_time:
            stale.append(assembly)
    if stale:
        print('STALE_ASSEMBLIES=' + ','.join(stale))
        return 1
    print('ASSEMBLIES_CURRENT=1')
    return 0


if __name__ == '__main__':
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    sys.exit(main(sys.argv[1], sys.argv[2]))
