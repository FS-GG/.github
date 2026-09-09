import subprocess

endpoint = "repos/FS-GG/.github/issues/1/comments"
subprocess.run(["gh", "api", "--method", "POST", endpoint], check=True)
