import json
import sys

path = sys.argv[1]
with open(path, encoding="utf-8") as handle:
    data = json.load(handle)

for item in sorted(data, key=lambda i: (i["box"]["cy"] if i["box"] else 0)):
    box = item["box"]
    if box and item["text"].strip():
        print("cy=%7.1f cx=%7.1f  %s" % (box["cy"], box["cx"], item["text"]))
